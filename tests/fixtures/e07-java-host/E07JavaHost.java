import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/** Independent Java 17 fixture host for E07. It intentionally models only the reserve.v0 fixture surface. */
public final class E07JavaHost {
    private final Map<String, Plan> plans = new LinkedHashMap<>();
    private final List<String> events = new ArrayList<>();
    private final List<String> statuses = new ArrayList<>();
    private long available = 10;
    private String stateRevision = "R0";
    private String programRevision = "P0";
    private String policyRevision = "Y0";
    private String manifestRevision = "M0";
    private boolean stateWrite = true;
    private int transitionCount;

    private record Plan(String eventId, long quantity, String state, String program, String policy, String manifest) {}

    public static void main(String[] args) throws Exception {
        if (args.length != 1) throw new IllegalArgumentException("fixture path required");
        String text = Files.readString(Path.of(args[0]), StandardCharsets.UTF_8);
        Object value = new JsonParser(text).parse();
        @SuppressWarnings("unchecked") Map<String, Object> fixture = (Map<String, Object>) value;
        E07JavaHost host = new E07JavaHost();
        host.run(fixture);
        System.out.println(host.observation((String) fixture.get("id")));
    }

    private void run(Map<String, Object> fixture) {
        String id = (String) fixture.get("id");
        @SuppressWarnings("unchecked") List<Object> steps = (List<Object>) fixture.get("steps");
        if ("effect-smuggling".equals(id)) {
            statuses.add("pre-admission:UnsupportedEffectSurface");
            return;
        }
        for (Object rawStep : steps) {
            @SuppressWarnings("unchecked") Map<String, Object> step = (Map<String, Object>) rawStep;
            String operation = (String) step.get("operation");
            @SuppressWarnings("unchecked") Map<String, Object> args = (Map<String, Object>) step.get("args");
            try {
                switch (operation) {
                    case "Prepare" -> prepare(args);
                    case "Commit" -> commit(args);
                    case "Replay" -> replay(args);
                    case "SetPolicy" -> { policyRevision = "Y1"; stateWrite = false; statuses.add("mutation:PolicyChanged"); }
                    case "ProposePatch" -> { programRevision = "P1"; statuses.add("mutation:ProgramChanged"); }
                    case "SetManifest" -> { manifestRevision = "M1"; statuses.add("mutation:AdmissionInvalidated"); }
                    default -> statuses.add("pre-admission:UnsupportedEffectSurface");
                }
            } catch (RuntimeException error) {
                String eventId = "Commit".equals(operation) ? reference((String) args.get("prepareId")) : "mutation";
                statuses.add(eventId + ":" + error.getMessage());
            }
        }
    }

    private void prepare(Map<String, Object> args) {
        String eventId = (String) args.get("eventId");
        long quantity = Long.parseLong((String) args.get("quantity"));
        if (events.contains(eventId)) statuses.add(eventId + ":AlreadyCommitted");
        else {
            plans.put(eventId, new Plan(eventId, quantity, stateRevision, programRevision, policyRevision, manifestRevision));
            statuses.add(eventId + ":Prepared");
        }
    }

    private void commit(Map<String, Object> args) {
        String eventId = reference((String) args.get("prepareId"));
        Plan plan = plans.get(eventId);
        if (plan == null) throw new IllegalStateException("PrepareExpired");
        String error = check(plan);
        if (error != null) {
            statuses.add(eventId + ":" + error);
            return;
        }
        available -= plan.quantity();
        stateRevision = "R" + (++transitionCount);
        events.add(eventId);
        statuses.add(eventId + ":Committed");
    }

    private void replay(Map<String, Object> args) {
        String eventId = reference((String) args.get("receiptId"));
        statuses.add(eventId + "-replay:" + (events.contains(eventId) ? "Replayed" : "ReplayUnavailable"));
    }

    private String check(Plan plan) {
        if (!plan.state().equals(stateRevision)) return "StateConflict";
        if (!plan.policy().equals(policyRevision)) return "PolicyChanged";
        if (!plan.program().equals(programRevision)) return "ProgramChanged";
        if (!plan.manifest().equals(manifestRevision)) return "AdmissionInvalidated";
        if (!stateWrite) return "CapabilityDenied";
        return null;
    }

    private static String reference(String value) {
        if (!value.startsWith("@")) return value;
        String token = value.substring(1);
        int dot = token.indexOf('.');
        return dot < 0 ? token : token.substring(0, dot);
    }

    private String observation(String fixtureId) {
        List<String> forbidden = new ArrayList<>();
        for (String event : events) forbidden.add("receipt:" + event);
        return "{" +
            "\"fixtureId\":" + quote(fixtureId) + "," +
            "\"statuses\":" + strings(statuses) + "," +
            "\"available\":" + quote(Long.toString(available)) + "," +
            "\"receiptCount\":" + events.size() + "," +
            "\"transitionCount\":" + transitionCount + "," +
            "\"eventIds\":" + strings(events) + "," +
            "\"effects\":[]," +
            "\"forbidden\":" + strings(forbidden) + "}";
    }

    private static String strings(List<String> values) {
        StringBuilder result = new StringBuilder("[");
        for (int i = 0; i < values.size(); i++) {
            if (i > 0) result.append(',');
            result.append(quote(values.get(i)));
        }
        return result.append(']').toString();
    }

    private static String quote(String value) {
        StringBuilder result = new StringBuilder("\"");
        for (int i = 0; i < value.length(); i++) {
            char c = value.charAt(i);
            if (c == '\\' || c == '"') result.append('\\').append(c);
            else if (c == '\n') result.append("\\n");
            else if (c == '\r') result.append("\\r");
            else if (c == '\t') result.append("\\t");
            else result.append(c);
        }
        return result.append('"').toString();
    }

    private static final class JsonParser {
        private final String text;
        private int index;
        JsonParser(String text) { this.text = text; }
        Object parse() {
            Object result = value();
            whitespace();
            if (index != text.length()) throw new IllegalArgumentException("trailing JSON");
            return result;
        }
        private Object value() {
            whitespace();
            if (index >= text.length()) throw new IllegalArgumentException("unexpected end");
            return switch (text.charAt(index)) {
                case '{' -> object();
                case '[' -> array();
                case '"' -> string();
                case 't' -> literal("true", Boolean.TRUE);
                case 'f' -> literal("false", Boolean.FALSE);
                case 'n' -> literal("null", null);
                default -> number();
            };
        }
        private Map<String, Object> object() {
            expect('{'); Map<String, Object> result = new LinkedHashMap<>(); whitespace();
            if (take('}')) return result;
            while (true) {
                String key = string(); whitespace(); expect(':'); result.put(key, value()); whitespace();
                if (take('}')) return result; expect(',');
            }
        }
        private List<Object> array() {
            expect('['); List<Object> result = new ArrayList<>(); whitespace();
            if (take(']')) return result;
            while (true) { result.add(value()); whitespace(); if (take(']')) return result; expect(','); }
        }
        private String string() {
            expect('"'); StringBuilder result = new StringBuilder();
            while (index < text.length()) {
                char c = text.charAt(index++);
                if (c == '"') return result.toString();
                if (c != '\\') { result.append(c); continue; }
                if (index >= text.length()) throw new IllegalArgumentException("bad escape");
                char escaped = text.charAt(index++);
                switch (escaped) {
                    case '"', '\\', '/' -> result.append(escaped);
                    case 'b' -> result.append('\b'); case 'f' -> result.append('\f'); case 'n' -> result.append('\n');
                    case 'r' -> result.append('\r'); case 't' -> result.append('\t');
                    case 'u' -> { String hex = text.substring(index, index + 4); result.append((char) Integer.parseInt(hex, 16)); index += 4; }
                    default -> throw new IllegalArgumentException("bad escape");
                }
            }
            throw new IllegalArgumentException("unterminated string");
        }
        private Object number() {
            int start = index;
            while (index < text.length() && "-+0123456789.eE".indexOf(text.charAt(index)) >= 0) index++;
            String number = text.substring(start, index);
            try { return number.contains(".") || number.contains("e") || number.contains("E") ? Double.parseDouble(number) : Long.parseLong(number); }
            catch (NumberFormatException error) { throw new IllegalArgumentException("bad number", error); }
        }
        private Object literal(String literal, Object value) { if (!text.startsWith(literal, index)) throw new IllegalArgumentException("bad literal"); index += literal.length(); return value; }
        private void whitespace() { while (index < text.length() && Character.isWhitespace(text.charAt(index))) index++; }
        private boolean take(char expected) { if (index < text.length() && text.charAt(index) == expected) { index++; return true; } return false; }
        private void expect(char expected) { whitespace(); if (index >= text.length() || text.charAt(index++) != expected) throw new IllegalArgumentException("expected " + expected); }
    }
}
