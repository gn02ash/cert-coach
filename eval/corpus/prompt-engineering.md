# Prompt Engineering Notes

## Writing system prompts

The system prompt sets the role, the audience and the rules that apply to every turn. State who the assistant is, who it is talking to, what it must and must not do, and what the output should look like. Put stable instructions in the system prompt and the per-request details in the user message.

Explain the reason behind a rule rather than shouting it. "Answer in two sentences, because the reply is shown in a narrow sidebar" works better than "BE BRIEF!!!", because the model can apply the reason to cases the rule did not anticipate.

## Few-shot examples

Few-shot prompting means showing the model a handful of input and output pairs before the real input. Three to five examples are usually enough. Make them diverse, so the model learns the pattern rather than copying one example, and keep their format identical to the output you want.

Examples are the most reliable way to steer tone and format. If the examples all use bullet lists, the answer will almost certainly be a bullet list too.

## Structured output with a JSON schema

When code consumes the answer, ask for structured output. With schema-constrained decoding the model can only produce JSON that matches the schema you provide, including required properties and allowed enum values.

Still validate the result in your code. A response can be cut off by the output token limit, and a schema cannot express every business rule, such as two dates that must be in order.

Keep schemas small and descriptive. A property named start_date with a description is easier for the model to fill correctly than a property named d1.

## Temperature and sampling

Temperature controls randomness. Lower values make the output more focused and repeatable, which suits extraction and classification. Higher values make it more varied, which can help brainstorming. Many newer reasoning models manage sampling themselves and expose an effort setting instead.

## Prompt injection

Any text the application did not write, such as a web page, an email or an uploaded document, may contain instructions aimed at the model. This is called prompt injection. Wrap untrusted content in clear delimiters, tell the model that the content is data and not instructions, and never give the model a tool that can do damage on the strength of untrusted text alone.

Defence in depth matters more than any single phrase: limit what tools can do, require human approval for risky actions, and check outputs before acting on them.

## Prompt caching

Prompt caching lets the provider reuse the processed form of a prompt prefix across requests. A cache hit makes the cached part of the prompt cheaper and faster, which pays off for long system prompts, tool definitions and documents that many requests share.

Caching matches on an exact prefix, so order the prompt from most stable to least stable: system prompt first, then tools and reference documents, then the conversation, and the new question last. Putting a timestamp at the start of the prompt changes the prefix on every request and silently disables caching.
