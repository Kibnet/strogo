import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.Base64;
import java.util.List;

import strogo.portable.v01.ModuleApi;

public final class Consumer {
  private Consumer() { }

  public static void main(String[] args) throws Exception {
    List<ExactCase> exactCases = List.of(
        new ExactCase("summarize", "{\"schema\":\"strogo.invoke.v0.1\",\"functionId\":\"summarize\",\"arguments\":[{\"kind\":\"sequence\",\"items\":[{\"kind\":\"i64\",\"value\":\"4\"},{\"kind\":\"i64\",\"value\":\"-3\"},{\"kind\":\"i64\",\"value\":\"1\"}]}]}", "{\"schema\":\"strogo.invoke-result.v0.1\",\"kind\":\"success\",\"value\":{\"kind\":\"record\",\"typeId\":\"Summary\",\"fields\":[{\"fieldId\":\"echo\",\"value\":{\"kind\":\"sequence\",\"items\":[{\"kind\":\"i64\",\"value\":\"4\"},{\"kind\":\"i64\",\"value\":\"-3\"},{\"kind\":\"i64\",\"value\":\"1\"}]}},{\"fieldId\":\"negativeCount\",\"value\":{\"kind\":\"i64\",\"value\":\"1\"}},{\"fieldId\":\"sum\",\"value\":{\"kind\":\"i64\",\"value\":\"2\"}}]}}"),
        new ExactCase("head-empty", request("headOrZero", sequence(List.of())), "{\"schema\":\"strogo.invoke-result.v0.1\",\"kind\":\"success\",\"value\":{\"kind\":\"i64\",\"value\":\"0\"}}"),
        new ExactCase("echo-summary", "{\"schema\":\"strogo.invoke.v0.1\",\"functionId\":\"echoSummary\",\"arguments\":[{\"kind\":\"record\",\"typeId\":\"Summary\",\"fields\":[{\"fieldId\":\"echo\",\"value\":{\"kind\":\"sequence\",\"items\":[]}},{\"fieldId\":\"negativeCount\",\"value\":{\"kind\":\"i64\",\"value\":\"0\"}},{\"fieldId\":\"sum\",\"value\":{\"kind\":\"i64\",\"value\":\"0\"}}]}]}", "{\"schema\":\"strogo.invoke-result.v0.1\",\"kind\":\"success\",\"value\":{\"kind\":\"record\",\"typeId\":\"Summary\",\"fields\":[{\"fieldId\":\"echo\",\"value\":{\"kind\":\"sequence\",\"items\":[]}},{\"fieldId\":\"negativeCount\",\"value\":{\"kind\":\"i64\",\"value\":\"0\"}},{\"fieldId\":\"sum\",\"value\":{\"kind\":\"i64\",\"value\":\"0\"}}]}}"),
        new ExactCase("owner-refusal", "{\"schema\":\"strogo.invoke.v0.1\",\"functionId\":\"adjust\",\"arguments\":[{\"kind\":\"bool\",\"value\":true},{\"kind\":\"i64\",\"value\":\"9223372036854775807\"}]}", "{\"schema\":\"strogo.invoke-result.v0.1\",\"kind\":\"refusal\",\"code\":\"OwnerPreconditionFailed\",\"locus\":\"function/adjust/requires\",\"details\":{}}"),
        new ExactCase("wrong-type", request("increment", "{\"kind\":\"bool\",\"value\":true}"), "{\"schema\":\"strogo.invoke-result.v0.1\",\"kind\":\"refusal\",\"code\":\"RuntimeTypeMismatch\",\"locus\":\"$/arguments/0\",\"details\":{}}"),
        new ExactCase("leading-zero", request("increment", "{\"kind\":\"i64\",\"value\":\"01\"}"), "{\"schema\":\"strogo.invoke-result.v0.1\",\"kind\":\"refusal\",\"code\":\"InvalidI64Encoding\",\"locus\":\"$/arguments/0/value\",\"details\":{\"value\":\"01\"}}"),
        new ExactCase("noncanonical", "{ \"schema\":\"strogo.invoke.v0.1\",\"functionId\":\"increment\",\"arguments\":[]}", refusal("NonCanonicalTransport", "$", "{}")),
        new ExactCase("malformed", "{", refusal("MalformedJson", "$", "{}")));
    for (ExactCase item : exactCases) {
      String actual = ModuleApi.invoke(item.input());
      if (!actual.equals(item.expected())) throw new IllegalStateException(item.id() + ": " + actual);
    }

    List<CodeCase> codeCases = new ArrayList<>();
    codeCases.add(new CodeCase("wrong-parameter-type", request("increment", "{\"kind\":\"bool\",\"value\":true}"), "RuntimeTypeMismatch"));
    codeCases.add(new CodeCase("missing-record-field", "{\"schema\":\"strogo.invoke.v0.1\",\"functionId\":\"echoSummary\",\"arguments\":[{\"kind\":\"record\",\"typeId\":\"Summary\",\"fields\":[{\"fieldId\":\"echo\",\"value\":{\"kind\":\"sequence\",\"items\":[]}},{\"fieldId\":\"negativeCount\",\"value\":{\"kind\":\"i64\",\"value\":\"0\"}}]}]}", "RuntimeTypeMismatch"));
    codeCases.add(new CodeCase("extra-record-field", "{\"schema\":\"strogo.invoke.v0.1\",\"functionId\":\"echoSummary\",\"arguments\":[{\"kind\":\"record\",\"typeId\":\"Summary\",\"fields\":[{\"fieldId\":\"echo\",\"value\":{\"kind\":\"sequence\",\"items\":[]}},{\"fieldId\":\"negativeCount\",\"value\":{\"kind\":\"i64\",\"value\":\"0\"}},{\"fieldId\":\"sum\",\"value\":{\"kind\":\"i64\",\"value\":\"0\"}},{\"fieldId\":\"x\",\"value\":{\"kind\":\"i64\",\"value\":\"0\"}}]}]}", "RuntimeTypeMismatch"));
    codeCases.add(new CodeCase("reordered-record-fields", "{\"schema\":\"strogo.invoke.v0.1\",\"functionId\":\"echoSummary\",\"arguments\":[{\"kind\":\"record\",\"typeId\":\"Summary\",\"fields\":[{\"fieldId\":\"sum\",\"value\":{\"kind\":\"i64\",\"value\":\"0\"}},{\"fieldId\":\"negativeCount\",\"value\":{\"kind\":\"i64\",\"value\":\"0\"}},{\"fieldId\":\"echo\",\"value\":{\"kind\":\"sequence\",\"items\":[]}}]}]}", "RuntimeTypeMismatch"));
    codeCases.add(new CodeCase("over-capacity-sequence", request("summarize", sequence(java.util.Collections.nCopies(9, "{\"kind\":\"i64\",\"value\":\"0\"}"))), "RuntimeTypeMismatch"));
    codeCases.add(new CodeCase("null-input", null, "NullRequest"));
    codeCases.add(new CodeCase("malformed-json", "{", "MalformedJson"));
    codeCases.add(new CodeCase("duplicate-property", "{\"schema\":\"strogo.invoke.v0.1\",\"schema\":\"strogo.invoke.v0.1\",\"functionId\":\"increment\",\"arguments\":[]}", "DuplicateProperty"));
    codeCases.add(new CodeCase("unknown-property", "{\"schema\":\"strogo.invoke.v0.1\",\"functionId\":\"increment\",\"arguments\":[],\"extra\":false}", "UnknownProperty"));
    codeCases.add(new CodeCase("missing-property", "{\"schema\":\"strogo.invoke.v0.1\",\"functionId\":\"increment\"}", "MissingProperty"));
    codeCases.add(new CodeCase("wrong-schema", "{\"schema\":\"strogo.invoke.v9\",\"functionId\":\"increment\",\"arguments\":[]}", "UnsupportedInvokeSchema"));
    codeCases.add(new CodeCase("array-root", "[]", "InvalidRoot"));
    codeCases.add(new CodeCase("unknown-wire-kind", request("increment", "{\"kind\":\"wat\"}"), "UnknownWireKind"));
    codeCases.add(new CodeCase("i64-leading-zero", request("increment", "{\"kind\":\"i64\",\"value\":\"01\"}"), "InvalidI64Encoding"));
    codeCases.add(new CodeCase("i64-invalid", request("increment", "{\"kind\":\"i64\",\"value\":\"x\"}"), "InvalidI64Encoding"));
    codeCases.add(new CodeCase("noncanonical-property-order", "{\"functionId\":\"increment\",\"schema\":\"strogo.invoke.v0.1\",\"arguments\":[]}", "NonCanonicalTransport"));
    codeCases.add(new CodeCase("utf8-65536", sizedUnknownFunction(65536), "UnknownFunction"));
    codeCases.add(new CodeCase("utf8-65537", sizedUnknownFunction(65537), "InputTooLarge"));
    codeCases.add(new CodeCase("depth-32", request("increment", nestedArrays(29)), "InvalidRoot"));
    codeCases.add(new CodeCase("depth-33", request("increment", nestedArrays(30)), "TransportDepthLimitExceeded"));
    codeCases.add(new CodeCase("nodes-2048", requestWithArguments(java.util.Collections.nCopies(2044, "false")), "InvalidRoot"));
    codeCases.add(new CodeCase("nodes-2049", requestWithArguments(java.util.Collections.nCopies(2045, "false")), "TransportValueLimitExceeded"));
    codeCases.add(new CodeCase("lone-surrogate", "\ud800", "InvalidUnicode"));
    codeCases.add(new CodeCase("lone-surrogate-over-limit", "\ud800" + "x".repeat(65537), "InvalidUnicode"));
    for (CodeCase item : codeCases) {
      String output = ModuleApi.invoke(item.input());
      String actual = outcomeCode(output);
      if (!actual.equals(item.code())) throw new IllegalStateException(item.id() + ": expected " + item.code() + ", actual " + output);
    }

    String escapedSurrogate = "{\"schema\":\"strogo.invoke.v0.1\",\"functionId\":\"" + "\\u" + "D800\",\"arguments\":[]}";
    if (!outcomeCode(ModuleApi.invoke(escapedSurrogate)).equals("MalformedJson")) throw new IllegalStateException("escaped lone surrogate was not rejected");
    System.out.println("PASS standalone Java consumer cases=" + exactCases.size() + " transport=" + codeCases.size() + " additional=1");

    if (args.length == 1) {
      int vectors = 0;
      Base64.Decoder decoder = Base64.getDecoder();
      for (String line : Files.readAllLines(Path.of(args[0]), StandardCharsets.UTF_8)) {
        String[] columns = line.split("\\t", -1);
        if (columns.length != 3) throw new IllegalStateException("invalid vector row");
        String request = new String(decoder.decode(columns[1]), StandardCharsets.UTF_8);
        String expected = new String(decoder.decode(columns[2]), StandardCharsets.UTF_8);
        String actual = ModuleApi.invoke(request);
        if (!actual.equals(expected)) throw new IllegalStateException("vector " + columns[0] + ": " + actual);
        vectors++;
      }
      if (vectors != 13) throw new IllegalStateException("expected 13 vectors, actual " + vectors);
      System.out.println("PASS standalone Java invoke vectors=" + vectors);
    }
  }

  private static String request(String functionId, String argument) { return "{\"schema\":\"strogo.invoke.v0.1\",\"functionId\":\"" + functionId + "\",\"arguments\":[" + argument + "]}"; }
  private static String requestWithArguments(List<String> arguments) { return "{\"schema\":\"strogo.invoke.v0.1\",\"functionId\":\"increment\",\"arguments\":[" + String.join(",", arguments) + "]}"; }
  private static String sequence(List<String> items) { return "{\"kind\":\"sequence\",\"items\":[" + String.join(",", items) + "]}"; }
  private static String nestedArrays(int count) { return "[".repeat(count) + "false" + "]".repeat(count); }
  private static String sizedUnknownFunction(int bytes) {
    String prefix = "{\"schema\":\"strogo.invoke.v0.1\",\"functionId\":\"";
    String suffix = "\",\"arguments\":[]}";
    return prefix + "x".repeat(bytes - prefix.length() - suffix.length()) + suffix;
  }
  private static String refusal(String code, String locus, String details) { return "{\"schema\":\"strogo.invoke-result.v0.1\",\"kind\":\"refusal\",\"code\":\"" + code + "\",\"locus\":\"" + locus + "\",\"details\":" + details + "}"; }
  private static String outcomeCode(String output) {
    if (output.startsWith("{\"schema\":\"strogo.invoke-result.v0.1\",\"kind\":\"success\",")) return "success";
    String marker = "\"code\":\""; int start = output.indexOf(marker); if (start < 0) throw new IllegalStateException("invalid outcome: " + output);
    start += marker.length(); int end = output.indexOf('"', start); if (end < 0) throw new IllegalStateException("invalid outcome: " + output); return output.substring(start, end);
  }

  private record ExactCase(String id, String input, String expected) { }
  private record CodeCase(String id, String input, String code) { }
}
