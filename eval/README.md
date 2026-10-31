# Retrieval evaluation

A small, fully synthetic test set for the document search behind **Search** and **Ask**. It answers two questions after any change to extraction, chunking, embeddings or ranking: does the right passage still come back near the top, and does Ask still decline questions the documents can't answer?

## What's here

| Path | Contents |
|---|---|
| `corpus/*.md` | Five study-note documents for the fictional DEMO-101 exam, written for this repository. The tool typesets them as PDFs (headings, wrapped paragraphs, page breaks, running footer) so they go through the same extraction as an upload. |
| `questions.jsonl` | One question per line. Answerable questions name the `document` and an `evidence` phrase from the passage that answers them; `"unanswerable": true` marks questions the corpus deliberately does not cover. |

Questions are labelled with an evidence phrase rather than a chunk ID, so the labels stay valid when chunking changes. Most are paraphrased, so keyword overlap alone does not find them.

## Run it

```bash
dotnet run --project tools/CertCoach.Eval -- --out eval-results/offline.md
```

This starts a throwaway pgvector container (Docker required), ingests the corpus through the real pipeline, and runs every question in vector, full-text and hybrid mode. To use your configured embeddings, set the same `AI_EMBEDDINGS_*` variables as the app, in the environment or in the repository's `.env`:

```bash
AI_EMBEDDINGS_PROVIDER=AzureOpenAI AI_EMBEDDINGS_ENDPOINT=https://<name>.openai.azure.com AI_EMBEDDINGS_MODEL=text-embedding-3-small AI_EMBEDDINGS_API_KEY=<key> dotnet run --project tools/CertCoach.Eval -- --out eval-results/azure.md
```

Other options:

| Option | Effect |
|---|---|
| `--connection "<postgres>"` | Use an existing database instead of a container. A new user is created, so existing data is left alone. |
| `--chunk-tokens <n>` | Try another chunk size. |
| `--min-recall5 <0..1>`, `--min-mrr <0..1>` | Exit with an error when hybrid search scores below these, for CI. |
| `--min-grounded <0..1>` | Exit with an error when fewer generated items than this are grounded (CI uses 1). |
| `--export <dir>` | Write the corpus as PDFs, for example to upload them and try Search and Ask. |
| `--dry-run` | Only typeset, extract and chunk the corpus, list the chunks, and check that every evidence phrase lands inside one chunk. No database. |

## Metrics

- **Recall@k:** the share of answerable questions whose evidence is in one of the top *k* passages.
- **MRR@10:** the mean of 1 / rank of the first correct passage (0 when it's not in the top 10). It rewards putting the answer first, which matters because Ask cites the top passages.
- **Gate:** for each question, the best cosine similarity among the passages Ask would use. Ask declines below its threshold (0.30 with real embeddings, 0.10 offline). The report shows how many answerable questions pass and unanswerable ones are declined, plus the threshold that would have separated this set best. Treat that number as a hint; on a set this small it will overfit.

- **Generated items:** practice items are written offline for each objective of the [DEMO-101 sample outline](../samples/DEMO-101-outline.md) from the corpus, as a generation run does without a chat model. An item counts as grounded when its prompt is the quoted sentence with the answer blanked out and that sentence is in the passage it cites. The report shows how many objectives the corpus covers and the share of items that are grounded.

CI runs the evaluation offline on every pull request and posts the report on the run summary. Offline embeddings measure shared words, not meaning, so expect paraphrased questions to rank lower there than with a real embedding model.

## Limits

The corpus is deliberately small (five documents, a dozen chunks), so recall@5 is easy to max out; recall@1 and MRR are the numbers to watch. It is a regression check, not a benchmark.
