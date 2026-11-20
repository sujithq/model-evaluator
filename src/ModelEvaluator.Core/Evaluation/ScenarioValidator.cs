using System.Text.Json;
using System.Text.Json.Nodes;
using ModelEvaluator.Core.Adapters;
using ModelEvaluator.Core.Results;
using ModelEvaluator.Core.Scenarios;

namespace ModelEvaluator.Core.Evaluation;

public static class ScenarioValidator
{
    public static IReadOnlyList<ValidationCheck> Validate(
        ScenarioDefinition scenario,
        IReadOnlyList<CallResult> calls,
        IReadOnlyList<TurnResult> turns,
        IReadOnlyList<ToolTraceEntry> toolTrace)
    {
        var checks = new List<ValidationCheck>();
        var finalError = calls.LastOrDefault(call => call.Error is not null)?.Error;
        var finalText = turns.LastOrDefault()?.AssistantMessage ?? "";

        if (finalError is not null)
        {
            var allowed = scenario.Validation.AllowedErrorCategories.Contains(
                finalError.Category,
                StringComparer.OrdinalIgnoreCase);
            checks.Add(new ValidationCheck
            {
                Id = "error-classification",
                Passed = allowed,
                Detail = allowed
                    ? $"The adapter classified the expected error as '{finalError.Category}' ({finalError.Code ?? "no code"})."
                    : $"Unexpected error category '{finalError.Category}': {finalError.Message}",
            });
            return checks;
        }

        if (scenario.Validation.AllowedErrorCategories.Count > 0
            && !scenario.Validation.AllowSuccessfulCompletionWhenErrorAllowed)
        {
            checks.Add(new ValidationCheck
            {
                Id = "expected-error",
                Passed = false,
                Detail = "The scenario expected an unsupported-parameter error, but the provider completed successfully.",
            });
        }
        else if (scenario.Validation.AllowedErrorCategories.Count > 0)
        {
            checks.Add(new ValidationCheck
            {
                Id = "successful-or-explicitly-classified-error",
                Passed = true,
                Detail = "The request completed successfully; the target accepted this parameter combination.",
            });
        }

        var finalCall = calls.LastOrDefault();
        if (finalCall is not null && !string.IsNullOrWhiteSpace(finalText))
        {
            var completed = string.Equals(finalCall.FinishReason, "stop", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(finalCall.FinishReason, "completed", StringComparison.OrdinalIgnoreCase);
            checks.Add(new ValidationCheck
            {
                Id = "completed-finish-reason",
                Passed = completed,
                Detail = completed
                    ? $"The final response completed with finish reason '{finalCall.FinishReason}'."
                    : $"The final response was not normally completed; finish reason was '{finalCall.FinishReason ?? "missing"}'.",
            });
        }

        foreach (var required in scenario.Validation.ContainsAll)
        {
            var passed = finalText.Contains(required, StringComparison.OrdinalIgnoreCase);
            checks.Add(new ValidationCheck
            {
                Id = $"contains:{required}",
                Passed = passed,
                Detail = passed
                    ? $"The final response contains '{required}'."
                    : $"The final response is missing required text '{required}'.",
            });
        }

        foreach (var requirement in scenario.Validation.ContainsAnyOf)
        {
            var matched = requirement.Terms.FirstOrDefault(
                term => finalText.Contains(term, StringComparison.OrdinalIgnoreCase));
            var passed = matched is not null;
            checks.Add(new ValidationCheck
            {
                Id = $"contains-any:{requirement.Id}",
                Passed = passed,
                Detail = passed
                    ? $"The final response satisfies '{requirement.Id}' using '{matched}'."
                    : $"The final response did not satisfy '{requirement.Id}'. Accepted wording: " +
                      string.Join(", ", requirement.Terms.Select(term => $"'{term}'")) + ".",
            });
        }

        foreach (var forbidden in scenario.Validation.DoesNotContain)
        {
            var passed = !finalText.Contains(forbidden, StringComparison.OrdinalIgnoreCase);
            checks.Add(new ValidationCheck
            {
                Id = $"excludes:{forbidden}",
                Passed = passed,
                Detail = passed
                    ? $"The final response excludes '{forbidden}'."
                    : $"The final response contains forbidden text '{forbidden}'.",
            });
        }

        if (scenario.Validation.ExactJson is JsonElement expected)
        {
            try
            {
                var actualNode = JsonNode.Parse(finalText);
                var expectedNode = JsonNode.Parse(expected.GetRawText());
                var passed = JsonNode.DeepEquals(actualNode, expectedNode);
                checks.Add(new ValidationCheck
                {
                    Id = "exact-json",
                    Passed = passed,
                    Detail = passed
                        ? "The final response is valid JSON and exactly matches the synthetic expected payload."
                        : $"The final response did not match the expected JSON payload. Expected: {expected.GetRawText()}",
                });
            }
            catch (JsonException exception)
            {
                checks.Add(new ValidationCheck
                {
                    Id = "exact-json",
                    Passed = false,
                    Detail = $"The final response was not valid JSON: {exception.Message}",
                });
            }
        }

        if (scenario.Validation.ExpectedToolName is { } expectedTool)
        {
            var trace = toolTrace.FirstOrDefault(entry =>
                string.Equals(entry.Name, expectedTool, StringComparison.Ordinal));
            var passed = trace is not null && trace.Error is null;
            checks.Add(new ValidationCheck
            {
                Id = $"tool:{expectedTool}",
                Passed = passed,
                Detail = passed
                    ? $"The model invoked deterministic mock tool '{expectedTool}'."
                    : $"The model did not invoke the required deterministic mock tool '{expectedTool}'.",
            });

            if (scenario.Validation.ExpectedToolArguments is JsonElement expectedArguments)
            {
                var argumentsPassed = trace is not null
                                      && JsonNode.DeepEquals(
                                          JsonNode.Parse(trace.Arguments.GetRawText()),
                                          JsonNode.Parse(expectedArguments.GetRawText()));
                checks.Add(new ValidationCheck
                {
                    Id = $"tool-arguments:{expectedTool}",
                    Passed = argumentsPassed,
                    Detail = argumentsPassed
                        ? $"The arguments for '{expectedTool}' exactly matched the synthetic fixture."
                        : $"The arguments for '{expectedTool}' did not match the synthetic fixture.",
                });
            }
        }

        if (toolTrace.Count > 0)
        {
            var invalidCalls = toolTrace.Where(entry => entry.Error is not null).ToList();
            checks.Add(new ValidationCheck
            {
                Id = "valid-mock-tool-calls",
                Passed = invalidCalls.Count == 0,
                Detail = invalidCalls.Count == 0
                    ? "Every model tool call mapped to a known deterministic tool with valid JSON arguments."
                    : string.Join(" ", invalidCalls.Select(entry => entry.Error)),
            });
        }

        if (checks.Count == 0)
        {
            checks.Add(new ValidationCheck
            {
                Id = "completion",
                Passed = !string.IsNullOrWhiteSpace(finalText),
                Detail = string.IsNullOrWhiteSpace(finalText)
                    ? "The model returned no final text."
                    : "The model returned a final text response.",
            });
        }

        return checks;
    }
}
