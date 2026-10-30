export interface SseMessage {
  event: string;
  data: string;
}

/**
 * Parses a server-sent event stream into messages. EventSource only supports GET, and asking needs a POST
 * with a body, so the stream is read from fetch instead.
 */
export async function* readSse(body: ReadableStream<Uint8Array>): AsyncGenerator<SseMessage> {
  const reader = body.getReader();
  const decoder = new TextDecoder();
  let buffer = "";

  for (;;) {
    const { value, done } = await reader.read();
    // stream: true keeps a multi-byte character split across chunks intact.
    buffer += decoder.decode(value, { stream: !done }).replace(/\r\n?/g, "\n");

    let end: number;
    while ((end = buffer.indexOf("\n\n")) >= 0) {
      const message = parseBlock(buffer.slice(0, end));
      buffer = buffer.slice(end + 2);
      if (message) yield message;
    }

    if (done) {
      const message = parseBlock(buffer);
      if (message) yield message;
      return;
    }
  }
}

function parseBlock(block: string): SseMessage | null {
  let event = "message";
  const data: string[] = [];
  for (const line of block.split("\n")) {
    if (line.startsWith(":")) continue;
    const colon = line.indexOf(":");
    const field = colon < 0 ? line : line.slice(0, colon);
    const value = colon < 0 ? "" : line.slice(colon + 1).replace(/^ /, "");
    if (field === "event") event = value;
    else if (field === "data") data.push(value);
  }
  return data.length > 0 ? { event, data: data.join("\n") } : null;
}
