# GPT-5.1 to GPT-6.2 coding and API migration matrix

**Last reviewed:** 2026-11-20

This matrix describes the 22 active GPT model IDs in
[`config/evaluation.foundry.responses.example.json`](../config/evaluation.foundry.responses.example.json)
and the 13 deployed EU Data Zone targets in
[`config/evaluation.foundry.responses.deployed.example.json`](../config/evaluation.foundry.responses.deployed.example.json).
It is a migration aid, not an assertion that catalog availability proves deployment availability,
capacity, or production suitability.

The repository uses **Responses API** for the all-model matrix because the Codex and pro entries in
the catalog are not interchangeable with Chat Completions. A direct REST and an official OpenAI .NET
SDK target exist for every model, with the exact model ID as the deployment ID.

## Read the effort as more than a model swap

A `gpt-5.1` migration can cross four independent boundaries:

1. **Model behavior:** change the deployment/model ID while keeping the API and request shape fixed.
2. **API behavior:** move Chat Completions requests and result types to Responses requests and output
   items. Structured output moves from `response_format` to `text.format`.
3. **State/agent behavior:** replay reasoning/function items, choose server-side
   `previous_response_id` chaining or application-managed `store=false` state, and bound tool loops.
4. **Deployment readiness:** separately prove the exact deployment exists in the intended region/SKU,
   is functional, and has quota/capacity.

The 22-model evaluation config isolates 1 and keeps the Responses wire shape fixed. Its REST-versus-SDK
pairs isolate the SDK/framework dimension. It does **not** prove that replacing a model ID alone also
tests a Chat-to-Responses migration or a production agent's state model.

Effort labels in the tables assume a production baseline that currently uses `gpt-5.1` with Chat
Completions and tools:

| Effort | Meaning |
| --- | --- |
| **Low** | Deployment/model-only change after parameter and result checks. |
| **Medium** | Model change plus request limits, reasoning/state, output handling, or evidence updates. |
| **High** | Requires a Chat-to-Responses migration, tool-loop redesign, unsupported-parameter remediation, or an API/framework/state migration. |

## Token-budget rule

The model catalog publishes a total context window and separate input/output ceilings. Those values
are **not additive guaranteed budgets**. Input, generated output, and hidden reasoning tokens consume
the available context, and a high `max_output_tokens` value does not reserve output space.

- A Responses request uses `max_output_tokens`.
- A Chat Completions request for reasoning models uses `max_completion_tokens`, not `max_tokens`.
- Reasoning tokens count toward output usage.
- Check `status` and `incomplete_details.reason`; HTTP success can still contain an incomplete response
  stopped by the token limit.
- Microsoft explicitly documents an approximately 922,000-token combined prompt/generation budget
  for the current `gpt-5.5` Responses implementation, even though its published context window is
  1,050,000. The remaining generation budget is approximately `922,000 - prompt tokens`.

The evaluator currently configures 800 output tokens for all matrix targets. That keeps cross-model
comparisons controlled; production budgets must be tuned from observed reasoning-token usage.

## GPT-5.1

| Exact model ID | Variant and documented purpose | Limits: total / input / output | Chat, Responses, and tools | Reasoning and state notes | Structured output | EU Data Zone state | Effort from `gpt-5.1` |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `gpt-5.1` | General baseline. | 400K / 272K / 128K | Both APIs; functions/tools and parallel calls documented. | Reasoning effort defaults to `none`. `none`, `low`, `medium`, and `high` are the practical supported set; `minimal`, `xhigh`, and `max` are not documented for this model. Earlier reasoning-context modes apply (`auto`/`current_turn`); do not assume `all_turns`. | Chat: `response_format`. Responses: `text.format`. | **Confirmed and capacity-confirmed** in the 13-model deployed config. | **Low** — baseline/control. |
| `gpt-5.1-codex` | Codex-optimized for Codex CLI and VS Code extension. | 400K / 272K / 128K | **Responses only**; functions/tools documented. Parallel calls listed in the model row. | `none`, `low`, `medium`, and `high` are documented in the reasoning guidance; default is not explicitly stated. | Responses `text.format`. | **Catalog-only; not-confirmed**. | **High** for a Chat app; **Medium** for an existing Responses app. |
| `gpt-5.1-codex-mini` | Codex-optimized smaller variant; Learn does not state a separate coding quality/latency contract. | 400K / 272K / 128K | **Responses only**. | Same base effort caveats. **Documentation conflict:** the model row says parallel tools, while the feature table marks parallel calls unsupported. Validate single-call and parallel-call behavior independently. | Responses `text.format`. | **Catalog-only; not-confirmed**. | **High**; the parallel-call ambiguity is material. |
| `gpt-5.1-codex-max` | Codex-optimized max variant; explicitly supports `xhigh`. | 400K / 272K / 128K | **Responses only**; functions/tools and parallel calls documented. | `xhigh` is explicitly supported. **Documentation conflict:** the model note says `none` is unsupported, while the reasoning feature footnote lists `gpt-5.1-codex-max` among models that support `none`. Test and pin the accepted value rather than assuming. `max` is not documented. | Responses `text.format`. | **Catalog-only; not-confirmed**. | **High**. |

## GPT-5.2 through GPT-5.4

| Exact model ID | Variant and documented purpose | Limits: total / input / output | Chat, Responses, and tools | Reasoning and state notes | Structured output | EU Data Zone state | Effort from `gpt-5.1` |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `gpt-5.2` | General GPT-5.2. | 400K / 272K / 128K | Both APIs; functions/tools and parallel calls documented. | `none` is documented for `gpt-5.2`; `low`, `medium`, and `high` are the normal supported effort range. No `minimal`, `xhigh`, or `max`; earlier state modes only. | Chat `response_format`; Responses `text.format`. | **Catalog-only; not-confirmed**. | **Low** for an unchanged Responses shape; **Medium** if changing parameters/state. |
| `gpt-5.2-codex` | Codex-optimized for Codex CLI and VS Code extension. | 400K / 272K / 128K | **Responses only**; functions/tools and parallel calls documented. | `low`, `medium`, and `high` are the conservative set. Exact default and `none` support are not stated for this Codex ID in the cited Learn text. | Responses `text.format`. | **Catalog-only; not-confirmed**. | **High** for a Chat app. |
| `gpt-5.3-codex` | Codex-optimized. | 400K / 272K / 128K | **Responses only**; functions/tools and parallel calls documented. | Reasoning supported; exact default and accepted effort set beyond the common low/medium/high family behavior must be validated. | Responses `text.format`. | **Catalog-only; not-confirmed**. | **High**. |
| `gpt-5.4` | General GPT-5.4. | 1,050K / 922K / 128K | Both APIs; functions/tools and parallel calls documented. | The GPT-5.4 family supports `all_turns`; `xhigh` is documented for GPT-5.4 and `none` is listed for the family. `minimal` and `max` are not supported. Exact default is not stated. | Both API placements. | **Confirmed and capacity-confirmed**. | **Medium**: larger context plus a different reasoning-context model. |
| `gpt-5.4-mini` | Mini variant; no distinct coding-purpose guarantee is published. | 400K / 272K / 128K | Both APIs. **Documentation conflict:** model row says parallel calls; feature table marks them unsupported. | Family effort/state notes apply, but the exact default and mini-specific behavior are not stated. | Both API placements. | **Catalog-only; not-confirmed**. | **High** if workflows use parallel calls. |
| `gpt-5.4-nano` | Nano variant; no official cost, latency, or quality ordering is stated in the cited model row. | 400K / 272K / 128K | Both APIs; functions/tools and parallel calls documented. | Family effort/state notes apply; exact default not stated. | Both API placements. | **Catalog-only; not-confirmed**. | **Medium**. |
| `gpt-5.4-pro` | Pro-labeled variant. The catalog does not define a distinct coding contract from the label alone. | 1,050K / 922K / 128K | **Responses API**; functions/tools and parallel calls are in the feature table. Chat Completions is not listed in the model row. | Family `xhigh`/`all_turns` guidance applies; do not copy the `gpt-5-pro` rule that only supports `high`, because that is a different model ID. Exact default is unverified. | Structured outputs are not explicitly listed in this model row; treat as **unverified** until scenario validation. | **Catalog-only; not-confirmed**. | **High**. |

## GPT-5.5 and GPT-5.6

| Exact model ID | Variant and documented purpose | Limits: total / input / output | Chat, Responses, and tools | Reasoning and state notes | Structured output | EU Data Zone state | Effort from `gpt-5.1` |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `gpt-5.5` | General GPT-5.5. | 1,050K / 922K / 128K | Both APIs; functions/tools and parallel calls documented. | `none`, `low`, `medium`, `high`, and `xhigh` are documented; no `max`; `all_turns` supported. Responses has the explicit effective 922K combined-budget caveat. | Both API placements. | **Confirmed and capacity-confirmed**. | **Medium**. |
| `gpt-5.6-sol` | Sol variant; uniquely lists Flex processing in addition to the shared GPT-5.6 capabilities. | 1,050K / 922K / 128K | Both APIs. **Chat function tools require `reasoning_effort=none`**; use Responses for normal reasoning+tools. | `none`, `low`, `medium`, `high`, `xhigh`, and Responses-only `max`; Responses `reasoning.mode=standard|pro`; standard default; `all_turns` default. | Both API placements. | **Confirmed and capacity-confirmed**. | **High** for Chat tools; **Medium** for Responses. |
| `gpt-5.6-terra` | Terra variant; no distinct coding-purpose claim beyond shared GPT-5.6 capabilities is stated. | 1,050K / 922K / 128K | Same GPT-5.6 Chat-tool constraint. | Same GPT-5.6 effort/mode/state behavior. | Both API placements. | **Confirmed and capacity-confirmed**. | **High** for Chat tools; **Medium** for Responses. |
| `gpt-5.6-luna` | Luna variant; the GPT-6 Luna Decisions API feature does **not** imply a Decisions feature for this model. | 1,050K / 922K / 128K | Same GPT-5.6 Chat-tool constraint. | Same GPT-5.6 effort/mode/state behavior. | Both API placements. | **Confirmed and capacity-confirmed**. | **High** for Chat tools; **Medium** for Responses. |

## GPT-6, GPT-6.1, and GPT-6.2

All seven rows below are published with a 1,050,000-token context window, 922,000 maximum input tokens,
128,000 maximum output tokens, text/image input and text output, reasoning, structured outputs, Chat
Completions, Responses, streaming, verbosity, and GPT-6-family reasoning controls.

| Exact model ID | Variant and documented purpose | Tools/API distinction | Reasoning and state notes | EU Data Zone state | Effort from `gpt-5.1` |
| --- | --- | --- | --- | --- | --- |
| `gpt-6-sol` | Sol label; no separate coding-purpose contract is stated. | Chat and Responses; functions/tools and parallel calls documented, subject to the GPT-5.6-and-later Chat tool/effort constraint. | `xhigh` and Responses-only `max` exist; `none` is not verified. Responses supports `reasoning.mode=standard|pro`; `all_turns` is the GPT-6 default. `temperature` and `top_p` are listed as supported in the GPT-6 feature table. | **Confirmed and capacity-confirmed**. | **High** for Chat reasoning+tools; **Medium** for Responses. |
| `gpt-6-astra` | Astra label. Learn notes that Astra may apply enhanced safety controls under elevated-risk conditions; do not generalize this to a benchmark score change. | Chat and Responses; functions/tools and parallel calls documented, subject to the same Chat constraint. | Same GPT-6 controls/state as Sol. | **Confirmed and capacity-confirmed**. | **High** for Chat reasoning+tools; **Medium** for Responses. |
| `gpt-6-luna` | Luna variant with the Decisions API in addition to Responses/Chat. | Chat and Responses; functions/tools and parallel calls documented, subject to the same Chat constraint. | Same GPT-6 controls/state; Decisions is a separate API evaluation, not part of the current Responses tool matrix. | **Confirmed and capacity-confirmed**. | **High** for Chat reasoning+tools; **Medium** for Responses. |
| `gpt-6.1-sol` | Sol variant. | Chat and Responses, but model docs specify functions, tools, and parallel calls **for Responses only**. | Same GPT-6 reasoning/state controls; use Responses for agent tool loops. | **Confirmed and capacity-confirmed**. | **High** for a Chat-tool app; **Medium** for Responses. |
| `gpt-6.1-terra` | Terra variant; no separate purpose is stated. | Chat and Responses; model row lists functions, tools, and parallel calls without the Responses-only qualifier, but the general Chat reasoning+tools constraint still applies. | Same GPT-6 reasoning/state controls. | **Confirmed and capacity-confirmed**. | **High** for Chat reasoning+tools; **Medium** for Responses. |
| `gpt-6.2-astra` | Astra label; no exact variant-specific coding contract beyond the row. | Chat and Responses, but functions/tools and parallel calls are **Responses only**. | Same GPT-6 effort/mode/state controls. Watermarking can affect performance, so benchmark workloads with and without it. | **Confirmed and capacity-confirmed**. | **High** for a Chat-tool app; **Medium** for Responses. |
| `gpt-6.2-sol` | Sol label; no exact variant-specific coding contract beyond the row. | Chat and Responses, but functions/tools and parallel calls are **Responses only**. | Same GPT-6 effort/mode/state controls and watermarking caveat. | **Confirmed and capacity-confirmed**. | **High** for a Chat-tool app; **Medium** for Responses. |

## Chat Completions to Responses migration

| Concern | Chat Completions | Responses |
| --- | --- | --- |
| Client | `OpenAI.Chat.ChatClient` | `OpenAI.Responses.ResponsesClient` |
| Request | `ChatCompletionOptions` plus `IEnumerable<ChatMessage>` | `CreateResponseOptions` plus `InputItems` |
| Output limit | `MaxOutputTokenCount` serializes the reasoning-model Chat limit, `max_completion_tokens`. | `MaxOutputTokenCount` serializes `max_output_tokens`. |
| Structured output | `ChatResponseFormat.CreateJsonSchemaFormat` on `ChatCompletionOptions.ResponseFormat` (`response_format`). | `ResponseTextFormat.CreateJsonSchemaFormat` on `ResponseTextOptions.TextFormat` (`text.format`). |
| Result | `ChatCompletion`, `Content`, `ToolCalls`, `FinishReason`. | `ResponseResult`, `GetOutputText()`, `OutputItems`, `Status`, `IncompleteStatusDetails`, token usage. |
| Tool call follow-up | Add an `AssistantChatMessage` and `ToolChatMessage` for each `tool_call_id`. | Replay required response items and add a `function_call_output` using the matching call ID. |
| Conversation state | Application resends message history. | Use `previous_response_id` for stored responses, or replay output items/input state for `store=false`. Do not use `previous_response_id` with an unstored response. |
| Reasoning state | Not represented as an assistant tool result alone. | Preserve encrypted reasoning items and original output-item order for stateless tool loops. Their contents stay opaque to the application. |

The repository's
[`OpenAiResponsesAdapter`](../src/ModelEvaluator.Core/Adapters/OpenAiResponsesAdapter.cs) and
[`DirectResponsesAdapter`](../src/ModelEvaluator.Core/Adapters/DirectResponsesAdapter.cs) demonstrate the
wire and .NET request mapping used by the evaluation matrix. The current evaluation state intentionally
stores visible conversation messages, function calls and function outputs; it does not yet preserve
opaque encrypted reasoning items. Therefore, the harness validates deterministic tool-loop behavior,
but a production agent must add response-item replay or server-side response chaining.

### C# shape comparison

This is a shape migration, not a plug-compatible overload:

```csharp
ChatClient chatClient = /* configured once */;
var chatOptions = new ChatCompletionOptions
{
    MaxOutputTokenCount = 800,
    ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
        "result",
        schema,
        jsonSchemaIsStrict: true)
};

ChatCompletion completion =
    await chatClient.CompleteChatAsync(messages, chatOptions, cancellationToken);
string text = completion.Content[0].Text;
```

```csharp
ResponsesClient responsesClient = /* configured once */;
var responseOptions = new CreateResponseOptions
{
    Model = deploymentId,
    MaxOutputTokenCount = 800,
    StoredOutputEnabled = false,
    TextOptions = new ResponseTextOptions
    {
        TextFormat = ResponseTextFormat.CreateJsonSchemaFormat(
            "result",
            schema,
            jsonSchemaIsStrict: true)
    }
};
responseOptions.InputItems.Add(ResponseItem.CreateUserMessageItem(prompt));

ResponseResult response =
    (await responsesClient.CreateResponseAsync(responseOptions, cancellationToken)).Value;
string text = response.GetOutputText();
```

For a tool follow-up, preserve the output items from the previous call in their original order, then
append the matching result:

```csharp
var next = new CreateResponseOptions
{
    Model = deploymentId,
    MaxOutputTokenCount = 800,
    StoredOutputEnabled = false
};

foreach (ResponseItem item in response.OutputItems)
{
    next.InputItems.Add(item);
}

next.InputItems.Add(
    ResponseItem.CreateFunctionCallOutputItem(toolCallId, toolResultJson));
```

Production implementations should also persist the returned opaque state securely and bound the
number of tool rounds. Do not log encrypted reasoning content or infer that `store=false` removes
every service-side operational retention requirement.

## Risky parameter combinations

| Family or transition | Parameter/risk | Required treatment |
| --- | --- | --- |
| GPT-5 reasoning models on Chat Completions | `max_tokens`, `temperature`, `top_p`, and penalty parameters | Use `max_completion_tokens`; remove sampling/penalty parameters unless the exact API/model support is separately verified. |
| `gpt-5.6` and later on Chat Completions | Function tools with non-`none` reasoning effort | The request fails. Use `reasoning_effort=none` for that Chat combination or move tool loops to Responses. |
| `gpt-6.1-sol`, `gpt-6.2-sol`, `gpt-6.2-astra` | Coding/tool calls on Chat Completions | Catalog rows scope tools and parallel tool calls to Responses. |
| `gpt-5.1-codex-mini`, `gpt-5.4-mini` | Parallel tool calls | Learn content conflicts. Acceptance-test the exact deployment; do not rely on the generic model-row capability. |
| `gpt-5.1-codex-max` | `reasoning_effort=none` | Learn content conflicts. Pin and test the accepted set. `xhigh` is explicitly documented. |
| `gpt-5.4-pro` | Structured outputs and default effort | The model row omits structured outputs and a separate variant default; scenario-test instead of inferring from the `pro` label or `gpt-5-pro`. |
| GPT-5.6 and GPT-6 families | `max`, `xhigh`, `reasoning.mode` | `max` is Responses-only and limited to GPT-5.6/GPT-6; `reasoning.mode=pro` increases work, latency, and token use. Keep it out of controlled model-only runs unless held fixed. |
| GPT-5.4 and later | `reasoning.context=all_turns` | Supported for GPT-5.4+, and default for GPT-5.6/GPT-6. Replay and token-budget behavior still differs from a simple text-only message history. |
| Any framework/abstraction adapter | `IChatClient`, Semantic Kernel, or connector conversion | Treat framework behavior as another comparison dimension. First compare direct REST versus the native SDK, then add the abstraction. |

## EU Data Zone and capacity state

Catalog capability, account deployment, region placement, and capacity are independent facts. The
current repository evidence is:

| Exact model IDs | EU Data Zone deployment state | Capacity evidence on the configured account |
| --- | --- | --- |
| `gpt-5.1` | Confirmed, DataZoneStandard, Sweden Central, area EUR, Succeeded | Capacity 5, 5K TPM, 50 RPM, verified 2026-11-20 |
| `gpt-5.4`, `gpt-5.5`, `gpt-5.6-sol`, `gpt-5.6-terra`, `gpt-5.6-luna` | Confirmed, DataZoneStandard, Sweden Central, area EUR, Succeeded | Capacity 5, 5K TPM, 5 RPM each, verified 2026-11-20 |
| `gpt-6-sol`, `gpt-6-astra`, `gpt-6-luna`, `gpt-6.1-sol`, `gpt-6.1-terra`, `gpt-6.2-sol`, `gpt-6.2-astra` | Confirmed, DataZoneStandard, Sweden Central, area EUR, Succeeded | Capacity 15, 15K TPM, 15 RPM each, verified 2026-11-20 |
| `gpt-5.1-codex`, `gpt-5.1-codex-mini`, `gpt-5.1-codex-max`, `gpt-5.2`, `gpt-5.2-codex`, `gpt-5.3-codex`, `gpt-5.4-mini`, `gpt-5.4-nano`, `gpt-5.4-pro` | Catalog-only; no current EU deployment claim | Capacity/rate limits not confirmed |

The deployed-only config is the live starting point. The 22-model config deliberately keeps catalog-only
targets selectable so unsupported combinations are observed explicitly instead of being omitted
silently; `not-confirmed` is not a pass.

## Migration checklist

1. Record the exact current deployment/model ID, API kind, API version, SDK/package version, endpoint
   shape, authentication kind, request parameters, and prompt/scenario hash.
2. Choose the comparison axis:
   - model only: keep endpoint, Responses API, adapter, auth, schema, tools, and parameters fixed;
   - adapter only: keep model/deployment and request semantics fixed;
   - API only: deliberately compare Chat Completions with Responses and expect a larger migration.
3. If moving from Chat to Responses, migrate structured output, token-limit names, response/result
   types, function-call item handling, and state/chaining.
4. Replace `messages`/`tool_call_id` assumptions with Responses `InputItems`,
   `FunctionCallResponseItem`, matching call IDs, and `FunctionCallOutputItem`.
5. Preserve required reasoning/output items for multi-turn tool loops, or use
   `previous_response_id` for stored responses. Do not mix stateless `store=false` with response IDs.
6. Keep request parameters fixed before attribution. Move unsupported `temperature`, `top_p`,
   penalties, `reasoning_effort`, `reasoning.mode`, or parallel tools into explicit matrix dimensions.
7. Run the deployed-only config first, using `maxParallel: 1`. Narrow `--targets` and `--scenarios`
   for a probe before the full matrix.
8. Verify EU Data Zone and capacity evidence independently from model capability. Re-check before a
   production deployment.

## Validation acceptance criteria

A candidate is ready for further production evaluation only when all of the following are true:

- The exact model/deployment target is selectable and controlled comparisons change one intended
  dimension.
- The request shape is valid for the exact API/model pair, and unsupported parameters are explicitly
  classified rather than silently dropped.
- Strict structured output is accepted and exact synthetic JSON is produced.
- Deterministic tool names, argument JSON, call IDs, tool outputs, and bounded round count all match.
- A multi-turn follow-up retains the required deterministic state.
- `status` is complete or the equivalent adapter success; an incomplete token-budget result is a
  failure, not an empty successful answer.
- Usage includes input/output and reasoning tokens when the service returns them; missing metrics stay
  `null`/unavailable rather than zero.
- REST versus SDK differences are recorded, not attributed to the model.
- EU Data Zone and capacity states are each `confirmed` with factual references. Catalog availability,
  a successful dry run, or an API 200 alone is insufficient.

## Official Microsoft Learn references

- [Foundry Models sold by Azure (model IDs, context limits, and capabilities)](https://learn.microsoft.com/azure/foundry/foundry-models/concepts/models-sold-directly-by-azure)
- [Data Zone Standard and region availability](https://learn.microsoft.com/azure/foundry/foundry-models/concepts/models-sold-directly-by-azure-region-availability)
- [Azure OpenAI reasoning models and API/feature support](https://learn.microsoft.com/azure/foundry/openai/how-to/reasoning)
- [Use the Azure OpenAI Responses API](https://learn.microsoft.com/azure/foundry/openai/how-to/responses)
- [Structured outputs (`response_format` versus `text.format`)](https://learn.microsoft.com/azure/foundry/openai/how-to/structured-outputs)
- [Work with Chat Completions models](https://learn.microsoft.com/azure/foundry/openai/how-to/chatgpt)
- [Upgrade from Chat Completions to Responses](https://learn.microsoft.com/azure/developer/ai/how-to/azure-openai-to-responses)
- [Azure OpenAI SDK language support](https://learn.microsoft.com/azure/foundry/openai/supported-languages)
