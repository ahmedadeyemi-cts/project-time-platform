#!/usr/bin/env python3
from pathlib import Path


def replace_once(source: str, old: str, new: str, label: str) -> str:
    count = source.count(old)
    if count != 1:
        raise SystemExit(f"Expected exactly one {label}; found {count}.")
    return source.replace(old, new, 1)


embedding_path = Path("src/backend/ProjectTime.Api/Ai/PulseAiPrivateEmbeddingClient.cs")
embedding = embedding_path.read_text(encoding="utf-8")
start_marker = "    private static IReadOnlyList<double[]> ParseVectors(\n"
end_marker = "    private static PulseAiPrivateEmbeddingResult Failure(\n"
start = embedding.find(start_marker)
end = embedding.find(end_marker, start)
if start < 0 or end < 0:
    raise SystemExit("Could not locate the private embedding response parser boundaries.")

parser = r'''    private static IReadOnlyList<double[]> ParseVectors(
        JsonElement root,
        int expectedCount)
    {
        if (expectedCount <= 0) return [];

        IReadOnlyList<double[]> vectors;
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("data", out var data))
        {
            vectors = ParseIndexedVectorObjects(data, expectedCount);
        }
        else if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("embeddings", out var embeddings))
        {
            vectors = ParseVectorCollection(embeddings, expectedCount);
        }
        else if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("embedding", out var embedding))
        {
            vectors = ParseSingleVector(embedding, expectedCount);
        }
        else if (root.ValueKind == JsonValueKind.Array)
        {
            vectors = ParseVectorCollection(root, expectedCount);
        }
        else
        {
            return [];
        }

        if (vectors.Count != expectedCount || vectors.Count == 0) return [];
        var dimension = vectors[0].Length;
        if (dimension == 0 || vectors.Any(vector => vector.Length != dimension)) return [];
        return vectors;
    }

    private static IReadOnlyList<double[]> ParseIndexedVectorObjects(
        JsonElement data,
        int expectedCount)
    {
        if (data.ValueKind != JsonValueKind.Array) return [];

        var indexed = new SortedDictionary<int, double[]>();
        var fallbackIndex = 0;
        foreach (var item in data.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || !item.TryGetProperty("embedding", out var embedding)
                || !TryParseVector(embedding, out var vector))
            {
                return [];
            }

            var index = item.TryGetProperty("index", out var indexProperty)
                && indexProperty.TryGetInt32(out var parsedIndex)
                ? parsedIndex
                : fallbackIndex;
            fallbackIndex++;
            if (index < 0 || index >= expectedCount || !indexed.TryAdd(index, vector))
            {
                return [];
            }
        }

        if (indexed.Count != expectedCount
            || !indexed.Keys.SequenceEqual(Enumerable.Range(0, expectedCount)))
        {
            return [];
        }

        return indexed.Values.ToArray();
    }

    private static IReadOnlyList<double[]> ParseVectorCollection(
        JsonElement collection,
        int expectedCount)
    {
        if (collection.ValueKind != JsonValueKind.Array) return [];

        if (TryParseVector(collection, out var singleVector))
        {
            return expectedCount == 1 ? [singleVector] : [];
        }

        var vectors = new List<double[]>();
        foreach (var item in collection.EnumerateArray())
        {
            JsonElement vectorElement;
            if (item.ValueKind == JsonValueKind.Object
                && item.TryGetProperty("embedding", out var embedded))
            {
                vectorElement = embedded;
            }
            else
            {
                vectorElement = item;
            }

            if (!TryParseVector(vectorElement, out var vector)) return [];
            vectors.Add(vector);
        }

        return vectors.Count == expectedCount ? vectors : [];
    }

    private static IReadOnlyList<double[]> ParseSingleVector(
        JsonElement embedding,
        int expectedCount) =>
        expectedCount == 1 && TryParseVector(embedding, out var vector)
            ? [vector]
            : [];

    private static bool TryParseVector(
        JsonElement element,
        out double[] vector)
    {
        vector = [];
        if (element.ValueKind != JsonValueKind.Array
            || element.GetArrayLength() == 0)
        {
            return false;
        }

        var values = new List<double>(element.GetArrayLength());
        foreach (var value in element.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.Number
                || !value.TryGetDouble(out var number)
                || double.IsNaN(number)
                || double.IsInfinity(number))
            {
                return false;
            }
            values.Add(number);
        }

        vector = values.ToArray();
        return vector.Length > 0;
    }

'''
embedding = embedding[:start] + parser + embedding[end:]
embedding_path.write_text(embedding, encoding="utf-8")

program_path = Path("tests/CelarAiOracleExternalRuntimeTests/Program.cs")
program = program_path.read_text(encoding="utf-8")
if "using System.Reflection;" not in program:
    program = replace_once(
        program,
        "using System.Net;\n",
        "using System.Net;\nusing System.Reflection;\nusing System.Text.Json;\n",
        "System.Net using directive",
    )

test_anchor = '    Console.WriteLine("CELAR_AI_ORACLE_EXTERNAL_HTTPS_RUNTIME_BEHAVIOR=PASS");'
tests = r'''    var embeddingParser = typeof(PulseAiPrivateEmbeddingClient).GetMethod(
        "ParseVectors",
        BindingFlags.NonPublic | BindingFlags.Static);
    Require(embeddingParser is not null, "private embedding response parser is available");

    RequireEmbeddingShape(
        embeddingParser!,
        "{\"data\":[{\"index\":0,\"embedding\":[0.1,0.2,0.3]}]}",
        1,
        3,
        "OpenAI data envelope is accepted");
    RequireEmbeddingShape(
        embeddingParser!,
        "[0.1,0.2,0.3]",
        1,
        3,
        "single raw vector is accepted");
    RequireEmbeddingShape(
        embeddingParser!,
        "[[0.1,0.2],[0.3,0.4]]",
        2,
        2,
        "raw vector collection is accepted");
    RequireEmbeddingShape(
        embeddingParser!,
        "{\"embeddings\":[[0.1,0.2,0.3]]}",
        1,
        3,
        "Ollama embeddings envelope is accepted");
    RequireEmbeddingShape(
        embeddingParser!,
        "{\"embedding\":[0.1,0.2,0.3]}",
        1,
        3,
        "single embedding envelope is accepted");
    RequireEmbeddingShape(
        embeddingParser!,
        "[{\"embedding\":[0.1,0.2,0.3]}]",
        1,
        3,
        "root array of embedding objects is accepted");
    RequireEmbeddingRejected(
        embeddingParser!,
        "[[0.1,0.2],[0.3]]",
        2,
        "inconsistent embedding dimensions are rejected");
    RequireEmbeddingRejected(
        embeddingParser!,
        "[0.1,0.2]",
        2,
        "single vector cannot satisfy a multi-input request");
    RequireEmbeddingRejected(
        embeddingParser!,
        "{\"data\":[{\"index\":1,\"embedding\":[0.1,0.2]}]}",
        1,
        "out-of-range OpenAI indices are rejected");

    Console.WriteLine("CELAR_AI_ORACLE_EMBEDDING_RESPONSE_COMPATIBILITY=PASS");
'''
program = replace_once(program, test_anchor, tests + test_anchor, "Oracle behavior completion marker")

helper_anchor = "static void ConfigureValidTestRuntime()\n"
helpers = r'''static IReadOnlyList<double[]> InvokeEmbeddingParser(
    MethodInfo parser,
    string json,
    int expectedCount)
{
    using var document = JsonDocument.Parse(json);
    return parser.Invoke(null, [document.RootElement, expectedCount])
        as IReadOnlyList<double[]> ?? [];
}

static void RequireEmbeddingShape(
    MethodInfo parser,
    string json,
    int expectedCount,
    int expectedDimension,
    string evidence)
{
    var vectors = InvokeEmbeddingParser(parser, json, expectedCount);
    Require(
        vectors.Count == expectedCount
        && vectors.All(vector => vector.Length == expectedDimension),
        evidence);
}

static void RequireEmbeddingRejected(
    MethodInfo parser,
    string json,
    int expectedCount,
    string evidence)
{
    var vectors = InvokeEmbeddingParser(parser, json, expectedCount);
    Require(vectors.Count == 0, evidence);
}

'''
program = replace_once(program, helper_anchor, helpers + helper_anchor, "ConfigureValidTestRuntime helper")
program_path.write_text(program, encoding="utf-8")

workflow_path = Path(".github/workflows/celar-ai-oracle-test-runtime-deploy.yml")
workflow = workflow_path.read_text(encoding="utf-8")
old_embedding_probe = r'''          curl -fsS --max-time 180 "${AUTH[@]}" -H 'Content-Type: application/json' \
            -d "$(jq -nc --arg model "$ORACLE_EMBEDDING_MODEL" '{model:$model,input:["Celar AI Test embedding proof"],encoding_format:"float"}')" \
            "$ORACLE_EMBEDDING_ENDPOINT" | jq -e '.data | length == 1 and (.data[0].embedding | length) == 768' >/dev/null
'''
new_embedding_probe = r'''          EMBEDDING_RESPONSE="$RUNNER_TEMP/oracle-embedding.json"
          curl -fsS --max-time 180 "${AUTH[@]}" -H 'Content-Type: application/json' \
            -d "$(jq -nc --arg model "$ORACLE_EMBEDDING_MODEL" '{model:$model,input:["Celar AI Test embedding proof"],encoding_format:"float"}')" \
            "$ORACLE_EMBEDDING_ENDPOINT" > "$EMBEDDING_RESPONSE"
          jq -e '
            def numeric_vector:
              type == "array"
              and length == 768
              and all(.[]; type == "number");
            if type == "object" and has("data") and (.data | type) == "array" then
              (.data | length == 1 and (.[0] | type) == "object" and (.[0].embedding | numeric_vector))
            elif type == "object" and has("embeddings") then
              (.embeddings | type == "array" and length == 1 and (.[0] | numeric_vector))
            elif type == "object" and has("embedding") then
              (.embedding | numeric_vector)
            elif type == "array" and numeric_vector then
              true
            elif type == "array" and length == 1 and (.[0] | numeric_vector) then
              true
            elif type == "array" and length == 1 and (.[0] | type) == "object" and (.[0] | has("embedding")) then
              (.[0].embedding | numeric_vector)
            else
              false
            end
          ' "$EMBEDDING_RESPONSE" >/dev/null
          rm -f "$EMBEDDING_RESPONSE"
'''
workflow = replace_once(workflow, old_embedding_probe, new_embedding_probe, "Oracle embedding preflight")
workflow = replace_once(
    workflow,
    '          DIGEST="$(scripts/build-pr55-acr-image.sh ',
    '          DIGEST="$(bash scripts/build-pr55-acr-image.sh ',
    "Oracle API image build invocation",
)
workflow_path.write_text(workflow, encoding="utf-8")

validator_path = Path("tests/validate-celar-ai-oracle-test-runtime.mjs")
validator = validator_path.read_text(encoding="utf-8")
contracts_anchor = "const contracts = read('src/backend/ProjectTime.Api/Ai/PulseAiPrivateRuntimeContracts.cs')\n"
validator = replace_once(
    validator,
    contracts_anchor,
    contracts_anchor + "const embeddingClient = read('src/backend/ProjectTime.Api/Ai/PulseAiPrivateEmbeddingClient.cs')\n",
    "Oracle validator contracts anchor",
)
runtime_anchor = "requireText(runtime, 'authenticated Test-only HTTPS malware scanning gateway', 'runtime readiness evidence')\n"
validator = replace_once(
    validator,
    runtime_anchor,
    runtime_anchor
    + "requireText(embeddingClient, 'ParseVectorCollection', 'multi-shape embedding response compatibility')\n"
    + "requireText(embeddingClient, 'ParseIndexedVectorObjects', 'OpenAI embedding response compatibility')\n"
    + "requireText(embeddingClient, 'TryParseVector', 'finite numeric vector validation')\n",
    "Oracle validator runtime anchor",
)
workflow_marker = "  'PRODUCTION_MUTATION=NONE',\n"
validator = replace_once(
    validator,
    workflow_marker,
    "  'oracle-embedding.json',\n"
    "  'numeric_vector',\n"
    "  'bash scripts/build-pr55-acr-image.sh',\n"
    + workflow_marker,
    "Oracle workflow marker list anchor",
)
validator_path.write_text(validator, encoding="utf-8")
