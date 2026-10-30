# Planning an AI Solution

## Choosing a model

Start from the task, not from the leaderboard. A model that is twice as capable is rarely worth twice the latency for a simple classification step. For routing, tagging and short extraction tasks, a small fast model usually meets the quality bar at a fraction of the cost. Reserve the largest models for open-ended reasoning, long documents and code generation, where their extra capability shows.

Write down three numbers before comparing models: the quality bar (measured on your own examples), the latency budget for one request, and the monthly budget. A model that misses any one of them is out, however well it scores elsewhere.

Context window size matters when prompts carry long documents or long conversations. A larger window does not mean you should fill it: every token in the prompt is billed and adds latency, so retrieve the relevant passages instead of sending whole manuals.

## Hosted models or self-hosted open models

A hosted model is called through an API. The provider runs the GPUs, applies updates and scales capacity, and you pay per token. This is the fastest way to start and the right default for most teams.

Self-hosting an open-weights model gives you full control over the model version and keeps all data inside your own network. The price is operational: you provision GPUs, handle scaling, patch the serving stack and monitor it at night. Self-hosting tends to pay off only with steady, high volume or strict rules that forbid sending data to a third party.

A middle path is a dedicated deployment from a provider: the model runs on capacity reserved for you, often in your chosen region, while the provider still operates it.

## Estimating tokens and cost

Models read and write tokens, not words. In English, one token is roughly three quarters of a word, so a 750-word page is about 1,000 tokens. Code and non-English text usually need more tokens per word.

Input tokens and output tokens are priced separately, and output is typically several times more expensive. A workload that sends long prompts and gets short answers is dominated by input cost; a workload that generates long reports is dominated by output cost.

A worked example: 20,000 requests a day, each with a 2,000-token prompt and a 300-token answer, is 40 million input tokens and 6 million output tokens per day. Multiply each by its price per million tokens and by 30 days to get the monthly estimate, then add a safety margin of 20 to 30 percent for retries and growth.

## Regional deployment and data residency

Deploy the model in the region closest to your users to keep latency low, and in a region allowed by your data residency rules. Some industries require that customer data, including prompts, never leaves a given country or economic area.

Plan a second region for resilience. If the primary region has an outage or runs out of capacity, traffic can fail over to the paired region, provided that region also satisfies the residency rules.

## Quotas and rate limits

Every deployment has quotas, usually expressed as tokens per minute and requests per minute. When an application exceeds them, the service answers with HTTP 429 Too Many Requests and a Retry-After header saying how long to wait.

Clients should retry 429 responses with exponential backoff and jitter, and honour the Retry-After value when it is present. Retrying immediately in a tight loop only makes the throttling last longer.

To raise throughput, request a higher quota, spread load across deployments in several regions, or reduce token usage by trimming prompts and caching results.

## Provisioned throughput

Pay-as-you-go pricing charges per token with no commitment, which suits variable or unpredictable traffic. Provisioned throughput reserves a fixed amount of model capacity for a monthly fee. It gives predictable latency and cost for steady, high-volume workloads, but you pay for the capacity whether you use it or not.
