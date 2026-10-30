import { describe, expect, it } from "vitest";
import { readSse, type SseMessage } from "./sse";

function streamOf(...chunks: string[]) {
  const encoder = new TextEncoder();
  return new ReadableStream<Uint8Array>({
    start(controller) {
      for (const chunk of chunks) controller.enqueue(encoder.encode(chunk));
      controller.close();
    },
  });
}

async function collect(stream: ReadableStream<Uint8Array>) {
  const messages: SseMessage[] = [];
  for await (const message of readSse(stream)) messages.push(message);
  return messages;
}

describe("readSse", () => {
  it("reads typed events split across chunks", async () => {
    const messages = await collect(streamOf('event: sources\ndata: {"a"', ':1}\n\nevent: delta\ndata: "hi"\n\n'));
    expect(messages).toEqual([
      { event: "sources", data: '{"a":1}' },
      { event: "delta", data: '"hi"' },
    ]);
  });

  it("joins multi-line data, skips comments and handles CRLF", async () => {
    const messages = await collect(streamOf(": keep-alive\r\n\r\ndata: one\r\ndata: two\r\n\r\n"));
    expect(messages).toEqual([{ event: "message", data: "one\ntwo" }]);
  });

  it("delivers a final event without a trailing blank line", async () => {
    expect(await collect(streamOf("event: done\ndata: {}"))).toEqual([{ event: "done", data: "{}" }]);
  });
});
