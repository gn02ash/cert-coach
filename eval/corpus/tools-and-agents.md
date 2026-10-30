# Tools and Agents

## Defining tools

A tool is a function the model can ask the application to run. Each tool has a name, a description and a JSON schema for its parameters. The model never runs the tool itself; it returns a tool call with arguments, and your code decides whether and how to execute it.

The description is the most important part. Say what the tool does, when to use it, and when not to. Give every parameter a type and a description, use enums for fixed choices, and mark which parameters are required. Vague descriptions are the most common reason a model picks the wrong tool.

## The agent loop

An agent is a model that uses tools in a loop until a task is done. The application sends the conversation and the tool definitions; the model either answers or requests one or more tool calls; the application runs them and sends the results back; and the cycle repeats.

Put a limit on the number of iterations, so a confused agent cannot loop forever and spend the whole budget. Log every tool call and result, because when an agent goes wrong the transcript is the only way to see why.

Some models request several independent tool calls in one turn. Run them in parallel and return all the results together in the next message.

## Handling tool errors

When a tool fails, return the error to the model as the tool result instead of crashing the loop. A clear message such as "customer 4021 not found" lets the model correct its arguments or try another approach. Return only what the model needs; a stack trace wastes tokens and can leak internal details.

## Model Context Protocol

The Model Context Protocol, or MCP, is an open standard for connecting AI applications to tools and data. An MCP server exposes tools, resources and prompts; any MCP client, such as a chat application or a coding assistant, can discover and call them.

Servers run either locally over standard input and output, or remotely over HTTP. Writing one MCP server for a capability means it can be reused by every compatible client instead of building a separate integration for each.

## Human approval

Tools that change things, such as sending email, moving money or deleting records, should require a person to approve the call before it runs. Show the user the exact action and arguments, and treat a timeout as a refusal. Read-only tools can usually run without approval.
