package strogo.portable.v01;

import java.math.BigInteger;
import java.nio.charset.StandardCharsets;
import java.util.ArrayDeque;
import java.util.ArrayList;
import java.util.Comparator;
import java.util.HashSet;
import java.util.List;
import java.util.Set;
import java.util.regex.Pattern;

import PortableWrapper.WireDetail;
import PortableWrapper.WireField;
import PortableWrapper.WireOutcome;
import PortableWrapper.WireValue;
import PortableWrapper.WireDetailValue;
import dafny.CodePoint;
import dafny.DafnySequence;

public final class ModuleApi {
  private static final int MAXIMUM_INPUT_BYTES = 65536;
  private static final int MAXIMUM_DEPTH = 32;
  private static final int MAXIMUM_VALUES = 2048;
  private static final Pattern CANONICAL_I64 = Pattern.compile("^(0|-?[1-9][0-9]*)$");
  private static final List<String> REFUSAL_PRIORITY = List.of(
      "MalformedJson", "TransportDepthLimitExceeded", "TransportValueLimitExceeded", "DuplicateProperty",
      "UnknownProperty", "MissingProperty", "UnsupportedInvokeSchema", "InvalidRoot", "UnknownWireKind",
      "InvalidI64Encoding", "NonCanonicalTransport");

  private ModuleApi() { }

  public static String invoke(String canonicalRequestJson) {
    if (canonicalRequestJson == null) return refusalJson("NullRequest", "$", List.of());
    if (!hasOnlyUnicodeScalars(canonicalRequestJson)) return refusalJson("InvalidUnicode", "$", List.of());
    int utf8Length = canonicalRequestJson.getBytes(StandardCharsets.UTF_8).length;
    if (utf8Length > MAXIMUM_INPUT_BYTES) {
      return refusalJson("InputTooLarge", "$", List.of(
          new Detail("actual", Integer.toString(utf8Length)), new Detail("max", Integer.toString(MAXIMUM_INPUT_BYTES))));
    }

    SyntaxInspection syntax = SyntaxInspector.inspect(canonicalRequestJson);
    if (syntax.malformed()) return refusalJson("MalformedJson", "$", List.of());
    if (syntax.depth() > MAXIMUM_DEPTH) {
      return refusalJson("TransportDepthLimitExceeded", "$", List.of(
          new Detail("actual", Integer.toString(syntax.depth())), new Detail("max", Integer.toString(MAXIMUM_DEPTH))));
    }
    if (syntax.values() > MAXIMUM_VALUES) {
      return refusalJson("TransportValueLimitExceeded", "$", List.of(
          new Detail("actual", Integer.toString(syntax.values())), new Detail("max", Integer.toString(MAXIMUM_VALUES))));
    }

    final JsonValue root;
    try {
      root = new JsonParser(canonicalRequestJson).parse();
    } catch (JsonFailure failure) {
      return refusalJson("MalformedJson", "$", List.of());
    }

    List<Finding> findings = new ArrayList<>();
    validateRequest(root, findings);
    if (!findings.isEmpty()) {
      Finding finding = findings.stream().min(Comparator
          .comparingInt((Finding item) -> REFUSAL_PRIORITY.indexOf(item.code()))
          .thenComparing(Finding::locus)).orElseThrow();
      return refusalJson(finding.code(), finding.locus(), finding.details());
    }

    JsonObject request = (JsonObject) root;
    String canonical = canonicalRequest(request);
    if (!canonicalRequestJson.equals(canonical)) return refusalJson("NonCanonicalTransport", "$", List.of());
    String functionId = ((JsonString) unique(request, "functionId")).value();
    List<WireValue> arguments = new ArrayList<>();
    for (JsonValue argument : ((JsonArray) unique(request, "arguments")).values()) arguments.add(buildWireValue(argument));
    WireOutcome outcome = PortableWrapper.__default.Invoke(text(functionId), DafnySequence.fromList(WireValue._typeDescriptor(), arguments));
    return outcomeJson(outcome);
  }

  private static boolean hasOnlyUnicodeScalars(String value) {
    for (int index = 0; index < value.length(); index++) {
      char current = value.charAt(index);
      if (Character.isHighSurrogate(current)) {
        if (++index >= value.length() || !Character.isLowSurrogate(value.charAt(index))) return false;
      } else if (Character.isLowSurrogate(current)) {
        return false;
      }
    }
    return true;
  }

  private static void validateRequest(JsonValue root, List<Finding> findings) {
    if (!(root instanceof JsonObject object)) {
      findings.add(invalidRoot("$", root));
      return;
    }
    validateObject(object, "$", List.of("schema", "functionId", "arguments"), findings);
    JsonValue schema = unique(object, "schema");
    if (schema != null) {
      String actual = schema instanceof JsonString string ? string.value() : canonicalValue(schema);
      if (!"strogo.invoke.v0.1".equals(actual)) findings.add(new Finding("UnsupportedInvokeSchema", "$/schema", List.of(
          new Detail("expected", "strogo.invoke.v0.1"), new Detail("actual", actual))));
    }
    JsonValue functionId = unique(object, "functionId");
    if (functionId != null && !(functionId instanceof JsonString)) findings.add(new Finding("NonCanonicalTransport", "$/functionId", List.of()));
    JsonValue arguments = unique(object, "arguments");
    if (arguments != null) {
      if (!(arguments instanceof JsonArray array)) findings.add(new Finding("NonCanonicalTransport", "$/arguments", List.of()));
      else for (int index = 0; index < array.values().size(); index++) validateWireValue(array.values().get(index), "$/arguments/" + index, findings);
    }
  }

  private static void validateWireValue(JsonValue value, String locus, List<Finding> findings) {
    if (!(value instanceof JsonObject object)) {
      findings.add(invalidRoot(locus, value));
      return;
    }
    JsonValue kindValue = unique(object, "kind");
    String kind = kindValue instanceof JsonString string ? string.value() : null;
    List<String> fields = switch (kind == null ? "" : kind) {
      case "i64", "bool" -> List.of("kind", "value");
      case "sequence" -> List.of("kind", "items");
      case "record" -> List.of("kind", "typeId", "fields");
      default -> List.of("kind");
    };
    validateObject(object, locus, fields, findings);
    if (!("i64".equals(kind) || "bool".equals(kind) || "sequence".equals(kind) || "record".equals(kind))) {
      String kindText = kindValue == null ? "" : kindValue instanceof JsonString string ? string.value() : canonicalValue(kindValue);
      findings.add(new Finding("UnknownWireKind", locus + "/kind", List.of(new Detail("kind", kindText))));
      return;
    }
    if ("i64".equals(kind)) {
      JsonValue raw = unique(object, "value");
      if (raw != null) {
        String encoded = raw instanceof JsonString string ? string.value() : canonicalValue(raw);
        boolean valid = raw instanceof JsonString && CANONICAL_I64.matcher(encoded).matches();
        if (valid) {
          try { new BigInteger(encoded); } catch (NumberFormatException exception) { valid = false; }
        }
        if (!valid) findings.add(new Finding("InvalidI64Encoding", locus + "/value", List.of(new Detail("value", encoded))));
      }
    } else if ("bool".equals(kind)) {
      JsonValue raw = unique(object, "value");
      if (raw != null && !(raw instanceof JsonBoolean)) findings.add(new Finding("NonCanonicalTransport", locus + "/value", List.of()));
    } else if ("sequence".equals(kind)) {
      JsonValue raw = unique(object, "items");
      if (raw != null) {
        if (!(raw instanceof JsonArray array)) findings.add(new Finding("NonCanonicalTransport", locus + "/items", List.of()));
        else for (int index = 0; index < array.values().size(); index++) validateWireValue(array.values().get(index), locus + "/items/" + index, findings);
      }
    } else {
      JsonValue typeId = unique(object, "typeId");
      if (typeId != null && !(typeId instanceof JsonString)) findings.add(new Finding("NonCanonicalTransport", locus + "/typeId", List.of()));
      JsonValue rawFields = unique(object, "fields");
      if (rawFields != null) {
        if (!(rawFields instanceof JsonArray array)) findings.add(new Finding("NonCanonicalTransport", locus + "/fields", List.of()));
        else for (int index = 0; index < array.values().size(); index++) validateWireField(array.values().get(index), locus + "/fields/" + index, findings);
      }
    }
  }

  private static void validateWireField(JsonValue value, String locus, List<Finding> findings) {
    if (!(value instanceof JsonObject object)) {
      findings.add(invalidRoot(locus, value));
      return;
    }
    validateObject(object, locus, List.of("fieldId", "value"), findings);
    JsonValue fieldId = unique(object, "fieldId");
    if (fieldId != null && !(fieldId instanceof JsonString)) findings.add(new Finding("NonCanonicalTransport", locus + "/fieldId", List.of()));
    JsonValue fieldValue = unique(object, "value");
    if (fieldValue != null) validateWireValue(fieldValue, locus + "/value", findings);
  }

  private static void validateObject(JsonObject object, String locus, List<String> expected, List<Finding> findings) {
    Set<String> seen = new HashSet<>();
    for (JsonProperty property : object.properties()) {
      if (!seen.add(property.name())) findings.add(new Finding("DuplicateProperty", locus, List.of(new Detail("property", property.name()))));
      if (!expected.contains(property.name())) findings.add(new Finding("UnknownProperty", locus, List.of(new Detail("property", property.name()))));
    }
    for (String name : expected) if (object.properties().stream().noneMatch(property -> property.name().equals(name)))
      findings.add(new Finding("MissingProperty", locus, List.of(new Detail("property", name))));
  }

  private static JsonValue unique(JsonObject object, String name) {
    JsonValue result = null;
    int count = 0;
    for (JsonProperty property : object.properties()) if (property.name().equals(name)) { result = property.value(); count++; }
    return count == 1 ? result : null;
  }

  private static Finding invalidRoot(String locus, JsonValue actual) {
    return new Finding("InvalidRoot", locus, List.of(new Detail("expected", "object"), new Detail("actual", kindName(actual))));
  }

  private static String kindName(JsonValue value) {
    if (value instanceof JsonObject) return "object";
    if (value instanceof JsonArray) return "array";
    if (value instanceof JsonString) return "string";
    if (value instanceof JsonNumber) return "number";
    if (value instanceof JsonBoolean) return "boolean";
    return "null";
  }

  private static WireValue buildWireValue(JsonValue value) {
    JsonObject object = (JsonObject) value;
    String kind = ((JsonString) unique(object, "kind")).value();
    return switch (kind) {
      case "i64" -> WireValue.create_WI64(new BigInteger(((JsonString) unique(object, "value")).value()));
      case "bool" -> WireValue.create_WBool(((JsonBoolean) unique(object, "value")).value());
      case "sequence" -> {
        List<WireValue> items = new ArrayList<>();
        for (JsonValue item : ((JsonArray) unique(object, "items")).values()) items.add(buildWireValue(item));
        yield WireValue.create_WSeq(DafnySequence.fromList(WireValue._typeDescriptor(), items));
      }
      case "record" -> {
        List<WireField> fields = new ArrayList<>();
        for (JsonValue item : ((JsonArray) unique(object, "fields")).values()) {
          JsonObject field = (JsonObject) item;
          fields.add(WireField.create(text(((JsonString) unique(field, "fieldId")).value()), buildWireValue(unique(field, "value"))));
        }
        yield WireValue.create_WRecord(text(((JsonString) unique(object, "typeId")).value()), DafnySequence.fromList(WireField._typeDescriptor(), fields));
      }
      default -> throw new IllegalStateException("validated wire kind became unavailable");
    };
  }

  private static String canonicalRequest(JsonObject request) {
    StringBuilder output = new StringBuilder();
    output.append('{');
    appendJsonString(output, "schema"); output.append(':'); appendJsonString(output, ((JsonString) unique(request, "schema")).value()); output.append(',');
    appendJsonString(output, "functionId"); output.append(':'); appendJsonString(output, ((JsonString) unique(request, "functionId")).value()); output.append(',');
    appendJsonString(output, "arguments"); output.append(':'); appendCanonicalArray(output, (JsonArray) unique(request, "arguments"), true);
    return output.append('}').toString();
  }

  private static void appendCanonicalWire(StringBuilder output, JsonObject object) {
    String kind = ((JsonString) unique(object, "kind")).value();
    output.append('{'); appendJsonString(output, "kind"); output.append(':'); appendJsonString(output, kind);
    if ("i64".equals(kind)) {
      output.append(','); appendJsonString(output, "value"); output.append(':'); appendJsonString(output, ((JsonString) unique(object, "value")).value());
    } else if ("bool".equals(kind)) {
      output.append(','); appendJsonString(output, "value"); output.append(':').append(((JsonBoolean) unique(object, "value")).value());
    } else if ("sequence".equals(kind)) {
      output.append(','); appendJsonString(output, "items"); output.append(':'); appendCanonicalArray(output, (JsonArray) unique(object, "items"), true);
    } else {
      output.append(','); appendJsonString(output, "typeId"); output.append(':'); appendJsonString(output, ((JsonString) unique(object, "typeId")).value());
      output.append(','); appendJsonString(output, "fields"); output.append(':').append('[');
      List<JsonValue> fields = ((JsonArray) unique(object, "fields")).values();
      for (int index = 0; index < fields.size(); index++) {
        if (index > 0) output.append(',');
        JsonObject field = (JsonObject) fields.get(index); output.append('{'); appendJsonString(output, "fieldId"); output.append(':');
        appendJsonString(output, ((JsonString) unique(field, "fieldId")).value()); output.append(','); appendJsonString(output, "value"); output.append(':');
        appendCanonicalWire(output, (JsonObject) unique(field, "value")); output.append('}');
      }
      output.append(']');
    }
    output.append('}');
  }

  private static void appendCanonicalArray(StringBuilder output, JsonArray array, boolean wireValues) {
    output.append('[');
    for (int index = 0; index < array.values().size(); index++) {
      if (index > 0) output.append(',');
      if (wireValues) appendCanonicalWire(output, (JsonObject) array.values().get(index));
      else output.append(canonicalValue(array.values().get(index)));
    }
    output.append(']');
  }

  private static String canonicalValue(JsonValue value) {
    StringBuilder output = new StringBuilder();
    appendValue(output, value);
    return output.toString();
  }

  private static void appendValue(StringBuilder output, JsonValue value) {
    if (value instanceof JsonString string) appendJsonString(output, string.value());
    else if (value instanceof JsonNumber number) output.append(number.encoded());
    else if (value instanceof JsonBoolean bool) output.append(bool.value());
    else if (value instanceof JsonNull) output.append("null");
    else if (value instanceof JsonArray array) {
      output.append('[');
      for (int index = 0; index < array.values().size(); index++) { if (index > 0) output.append(','); appendValue(output, array.values().get(index)); }
      output.append(']');
    } else {
      output.append('{'); List<JsonProperty> properties = ((JsonObject) value).properties();
      for (int index = 0; index < properties.size(); index++) { if (index > 0) output.append(','); appendJsonString(output, properties.get(index).name()); output.append(':'); appendValue(output, properties.get(index).value()); }
      output.append('}');
    }
  }

  private static String outcomeJson(WireOutcome outcome) {
    StringBuilder output = new StringBuilder("{\"schema\":\"strogo.invoke-result.v0.1\",");
    if (outcome.is_Success()) {
      output.append("\"kind\":\"success\",\"value\":"); appendWire(output, outcome.dtor_value());
    } else {
      output.append("\"kind\":\"refusal\",\"code\":"); appendJsonString(output, string(outcome.dtor_code()));
      output.append(",\"locus\":"); appendJsonString(output, string(outcome.dtor_locus())); output.append(",\"details\":{");
      DafnySequence<? extends WireDetail> details = outcome.dtor_details();
      for (int index = 0; index < details.length(); index++) {
        if (index > 0) output.append(','); WireDetail detail = details.select(index); appendJsonString(output, string(detail.dtor_key())); output.append(':');
        WireDetailValue detailValue = detail.dtor_detailValue(); appendJsonString(output, detailValue.is_DText() ? string(detailValue.dtor_text()) : detailValue.dtor_value().toString());
      }
      output.append('}');
    }
    return output.append('}').toString();
  }

  private static String refusalJson(String code, String locus, List<Detail> details) {
    StringBuilder output = new StringBuilder("{\"schema\":\"strogo.invoke-result.v0.1\",\"kind\":\"refusal\",\"code\":");
    appendJsonString(output, code); output.append(",\"locus\":"); appendJsonString(output, locus); output.append(",\"details\":{");
    for (int index = 0; index < details.size(); index++) {
      if (index > 0) output.append(','); appendJsonString(output, details.get(index).key()); output.append(':'); appendJsonString(output, details.get(index).value());
    }
    return output.append("}}").toString();
  }

  private static void appendWire(StringBuilder output, WireValue value) {
    output.append('{');
    if (value.is_WI64()) { output.append("\"kind\":\"i64\",\"value\":"); appendJsonString(output, value.dtor_i64Value().toString()); }
    else if (value.is_WBool()) output.append("\"kind\":\"bool\",\"value\":").append(value.dtor_boolValue());
    else if (value.is_WSeq()) {
      output.append("\"kind\":\"sequence\",\"items\":["); DafnySequence<? extends WireValue> items = value.dtor_sequenceItems();
      for (int index = 0; index < items.length(); index++) { if (index > 0) output.append(','); appendWire(output, items.select(index)); }
      output.append(']');
    } else {
      output.append("\"kind\":\"record\",\"typeId\":"); appendJsonString(output, string(value.dtor_recordTypeId())); output.append(",\"fields\":[");
      DafnySequence<? extends WireField> fields = value.dtor_recordFields();
      for (int index = 0; index < fields.length(); index++) {
        if (index > 0) output.append(','); WireField field = fields.select(index); output.append("{\"fieldId\":"); appendJsonString(output, string(field.dtor_fieldId())); output.append(",\"value\":"); appendWire(output, field.dtor_fieldValue()); output.append('}');
      }
      output.append(']');
    }
    output.append('}');
  }

  private static void appendJsonString(StringBuilder output, String value) {
    output.append('"');
    for (int index = 0; index < value.length(); index++) {
      char current = value.charAt(index);
      switch (current) {
        case '"' -> output.append("\\\""); case '\\' -> output.append("\\\\"); case '\b' -> output.append("\\b");
        case '\f' -> output.append("\\f"); case '\n' -> output.append("\\n"); case '\r' -> output.append("\\r"); case '\t' -> output.append("\\t");
        default -> { if (current < 0x20) output.append(String.format("\\u%04x", (int) current)); else output.append(current); }
      }
    }
    output.append('"');
  }

  private static DafnySequence<CodePoint> text(String value) { return DafnySequence.asUnicodeString(value); }
  private static String string(DafnySequence<? extends CodePoint> value) { return value.verbatimString(); }

  private sealed interface JsonValue permits JsonObject, JsonArray, JsonString, JsonNumber, JsonBoolean, JsonNull { }
  private record JsonObject(List<JsonProperty> properties) implements JsonValue { }
  private record JsonProperty(String name, JsonValue value) { }
  private record JsonArray(List<JsonValue> values) implements JsonValue { }
  private record JsonString(String value) implements JsonValue { }
  private record JsonNumber(String encoded) implements JsonValue { }
  private record JsonBoolean(boolean value) implements JsonValue { }
  private enum JsonNull implements JsonValue { INSTANCE }
  private record Detail(String key, String value) { }
  private record Finding(String code, String locus, List<Detail> details) { }
  private record SyntaxInspection(boolean malformed, int depth, int values) { }

  private static final class JsonFailure extends RuntimeException {
    private static final long serialVersionUID = 1L;
  }

  private static final class JsonParser {
    private final String source;
    private int index;

    JsonParser(String source) { this.source = source; }

    JsonValue parse() {
      JsonValue value = parseValue(); skipWhitespace(); if (index != source.length()) throw new JsonFailure(); return value;
    }

    private JsonValue parseValue() {
      skipWhitespace(); if (index >= source.length()) throw new JsonFailure(); char current = source.charAt(index);
      if (current == '{') return parseObject(); if (current == '[') return parseArray(); if (current == '"') return new JsonString(parseString());
      if (current == 't') { literal("true"); return new JsonBoolean(true); } if (current == 'f') { literal("false"); return new JsonBoolean(false); }
      if (current == 'n') { literal("null"); return JsonNull.INSTANCE; } return new JsonNumber(parseNumber());
    }

    private JsonObject parseObject() {
      index++; skipWhitespace(); List<JsonProperty> properties = new ArrayList<>(); if (take('}')) return new JsonObject(properties);
      while (true) {
        skipWhitespace(); if (index >= source.length() || source.charAt(index) != '"') throw new JsonFailure(); String name = parseString();
        skipWhitespace(); require(':'); JsonValue value = parseValue(); properties.add(new JsonProperty(name, value)); skipWhitespace();
        if (take('}')) return new JsonObject(properties); require(',');
      }
    }

    private JsonArray parseArray() {
      index++; skipWhitespace(); List<JsonValue> values = new ArrayList<>(); if (take(']')) return new JsonArray(values);
      while (true) { values.add(parseValue()); skipWhitespace(); if (take(']')) return new JsonArray(values); require(','); }
    }

    private String parseString() {
      require('"'); StringBuilder value = new StringBuilder();
      while (index < source.length()) {
        char current = source.charAt(index++); if (current == '"') return value.toString(); if (current < 0x20) throw new JsonFailure();
        if (current != '\\') { value.append(current); continue; }
        if (index >= source.length()) throw new JsonFailure(); char escaped = source.charAt(index++);
        switch (escaped) {
          case '"', '\\', '/' -> value.append(escaped); case 'b' -> value.append('\b'); case 'f' -> value.append('\f');
          case 'n' -> value.append('\n'); case 'r' -> value.append('\r'); case 't' -> value.append('\t');
          case 'u' -> {
            char first = unicodeUnit();
            if (Character.isHighSurrogate(first)) {
              if (index + 1 >= source.length() || source.charAt(index) != '\\' || source.charAt(index + 1) != 'u') throw new JsonFailure();
              index += 2; char second = unicodeUnit(); if (!Character.isLowSurrogate(second)) throw new JsonFailure(); value.append(first).append(second);
            } else { if (Character.isLowSurrogate(first)) throw new JsonFailure(); value.append(first); }
          }
          default -> throw new JsonFailure();
        }
      }
      throw new JsonFailure();
    }

    private char unicodeUnit() {
      if (index + 4 > source.length()) throw new JsonFailure(); int value = 0;
      for (int count = 0; count < 4; count++) { int digit = Character.digit(source.charAt(index++), 16); if (digit < 0) throw new JsonFailure(); value = value * 16 + digit; }
      return (char) value;
    }

    private String parseNumber() {
      int start = index; if (take('-') && index >= source.length()) throw new JsonFailure();
      if (take('0')) { if (index < source.length() && Character.isDigit(source.charAt(index))) throw new JsonFailure(); }
      else { if (index >= source.length() || source.charAt(index) < '1' || source.charAt(index) > '9') throw new JsonFailure(); while (index < source.length() && Character.isDigit(source.charAt(index))) index++; }
      if (take('.')) { if (index >= source.length() || !Character.isDigit(source.charAt(index))) throw new JsonFailure(); while (index < source.length() && Character.isDigit(source.charAt(index))) index++; }
      if (index < source.length() && (source.charAt(index) == 'e' || source.charAt(index) == 'E')) {
        index++; if (index < source.length() && (source.charAt(index) == '+' || source.charAt(index) == '-')) index++;
        if (index >= source.length() || !Character.isDigit(source.charAt(index))) throw new JsonFailure(); while (index < source.length() && Character.isDigit(source.charAt(index))) index++;
      }
      return source.substring(start, index);
    }

    private void literal(String value) { if (!source.startsWith(value, index)) throw new JsonFailure(); index += value.length(); }
    private boolean take(char expected) { if (index < source.length() && source.charAt(index) == expected) { index++; return true; } return false; }
    private void require(char expected) { if (!take(expected)) throw new JsonFailure(); }
    private void skipWhitespace() { while (index < source.length() && " \t\r\n".indexOf(source.charAt(index)) >= 0) index++; }
  }

  private static final class SyntaxInspector {
    private final String source;
    private final ArrayDeque<Frame> stack = new ArrayDeque<>();
    private int index;
    private int values;
    private int maxDepth;
    private boolean rootStarted;
    private boolean rootComplete;

    private SyntaxInspector(String source) { this.source = source; }

    static SyntaxInspection inspect(String source) {
      try { return new SyntaxInspector(source).run(); } catch (JsonFailure failure) { return new SyntaxInspection(true, 0, 0); }
    }

    private SyntaxInspection run() {
      while (true) {
        skipWhitespace();
        if (stack.isEmpty()) {
          if (!rootStarted) { rootStarted = true; startValue(1); continue; }
          if (!rootComplete || index != source.length()) throw new JsonFailure();
          return new SyntaxInspection(false, maxDepth, values);
        }
        Frame frame = stack.peek();
        if (frame.object) inspectObject(frame); else inspectArray(frame);
      }
    }

    private void inspectObject(Frame frame) {
      if (frame.state == 0 || frame.state == 1) {
        if (frame.state == 0 && take('}')) { stack.pop(); completeValue(); return; }
        scanString(); skipWhitespace(); require(':'); frame.state = 2; return;
      }
      if (frame.state == 2) { startValue(stack.size() + 1); return; }
      if (take('}')) { stack.pop(); completeValue(); return; }
      require(','); frame.state = 1;
    }

    private void inspectArray(Frame frame) {
      if (frame.state == 0 && take(']')) { stack.pop(); completeValue(); return; }
      if (frame.state == 0 || frame.state == 1) { startValue(stack.size() + 1); return; }
      if (take(']')) { stack.pop(); completeValue(); return; }
      require(','); frame.state = 1;
    }

    private void startValue(int depth) {
      skipWhitespace(); if (index >= source.length()) throw new JsonFailure(); values++; maxDepth = Math.max(maxDepth, depth); char current = source.charAt(index);
      if (current == '{') { index++; stack.push(new Frame(true)); return; }
      if (current == '[') { index++; stack.push(new Frame(false)); return; }
      if (current == '"') scanString();
      else if (current == 't') literal("true"); else if (current == 'f') literal("false"); else if (current == 'n') literal("null"); else scanNumber();
      completeValue();
    }

    private void completeValue() {
      if (stack.isEmpty()) rootComplete = true; else { Frame parent = stack.peek(); if (parent.state != 2 && parent.state != 0 && parent.state != 1) throw new JsonFailure(); parent.state = 3; }
    }

    private void scanString() {
      require('"');
      while (index < source.length()) {
        char current = source.charAt(index++); if (current == '"') return; if (current < 0x20) throw new JsonFailure();
        if (current == '\\') { if (index >= source.length()) throw new JsonFailure(); char escaped = source.charAt(index++); if (escaped == 'u') { for (int count = 0; count < 4; count++) if (index >= source.length() || Character.digit(source.charAt(index++), 16) < 0) throw new JsonFailure(); } else if ("\"\\/bfnrt".indexOf(escaped) < 0) throw new JsonFailure(); }
      }
      throw new JsonFailure();
    }

    private void scanNumber() {
      if (take('-') && index >= source.length()) throw new JsonFailure();
      if (take('0')) { if (index < source.length() && Character.isDigit(source.charAt(index))) throw new JsonFailure(); }
      else { if (index >= source.length() || source.charAt(index) < '1' || source.charAt(index) > '9') throw new JsonFailure(); while (index < source.length() && Character.isDigit(source.charAt(index))) index++; }
      if (take('.')) { if (index >= source.length() || !Character.isDigit(source.charAt(index))) throw new JsonFailure(); while (index < source.length() && Character.isDigit(source.charAt(index))) index++; }
      if (index < source.length() && (source.charAt(index) == 'e' || source.charAt(index) == 'E')) {
        index++; if (index < source.length() && (source.charAt(index) == '+' || source.charAt(index) == '-')) index++;
        if (index >= source.length() || !Character.isDigit(source.charAt(index))) throw new JsonFailure(); while (index < source.length() && Character.isDigit(source.charAt(index))) index++;
      }
    }
    private void literal(String value) { if (!source.startsWith(value, index)) throw new JsonFailure(); index += value.length(); }
    private boolean take(char expected) { if (index < source.length() && source.charAt(index) == expected) { index++; return true; } return false; }
    private void require(char expected) { if (!take(expected)) throw new JsonFailure(); }
    private void skipWhitespace() { while (index < source.length() && " \t\r\n".indexOf(source.charAt(index)) >= 0) index++; }

    private static final class Frame {
      final boolean object;
      int state;
      Frame(boolean object) { this.object = object; }
    }
  }
}
