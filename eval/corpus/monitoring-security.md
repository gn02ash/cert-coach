# Monitoring and Securing AI Workloads

## Observability

Trace every model request with its latency, the number of input and output tokens, the model version and the outcome. OpenTelemetry has semantic conventions for generative AI spans, so these traces can flow into the same monitoring system as the rest of the application.

Be careful with logging prompts and responses. They often contain personal data, so redact personal information before storing them, restrict who can read the logs, and keep them only as long as needed.

## Content safety

Content filters classify prompts and completions into harm categories such as hate, violence, sexual content and self-harm, each with a severity level. The application blocks or flags content above the threshold set for its audience. A children's tutoring app needs much stricter thresholds than an internal tool for security analysts.

Filters reduce risk but do not remove it. Combine them with a clear system prompt, limited tool permissions and a way for users to report bad answers.

## Managed identities and secrets

Never put API keys in source code or configuration files that are committed. Use a managed identity, so the application authenticates to cloud services with an identity the platform manages and no secret is stored at all.

When a secret is unavoidable, keep it in a secrets store such as a key vault, grant access with role assignments, and rotate it on a schedule. Give each service the least privilege it needs: an application that only calls the model does not need permission to change its deployment.

## Network isolation

Disable public network access to the AI service when only internal applications use it, and connect through private endpoints so traffic stays on the private network. Combine this with firewall rules and separate environments for development and production.

## Red teaming and responsible AI

Before release, red team the application: deliberately try to make it produce harmful output, leak its instructions or misuse its tools. Record what worked, fix it, and repeat the exercise after significant changes. Document the intended use, known limitations and the human oversight in place.

## Cost monitoring

Set budgets with alerts at 50, 80 and 100 percent of the expected monthly spend. Tag resources and record token usage per feature, so a sudden increase can be traced to the change that caused it rather than discovered on the invoice.
