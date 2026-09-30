# Hugging Face Parquet Browser

A small ASP.NET Core MVC app for public Hugging Face datasets. It lists Parquet shards through the Hugging Face Dataset Viewer, streams selected files into repository-specific folders, tracks progress in memory, and previews remote or downloaded rows.

## Requirements

- .NET 10 SDK
- Network access to `datasets-server.huggingface.co` and Hugging Face's file/CDN hosts

## Run locally

```bash
dotnet restore
dotnet run --urls http://0.0.0.0:3000
```

Open the app at `http://localhost:3000`. No Hugging Face token or database is required for public repositories. The home page expects a dataset ID such as `cornell-movie-review-data/rotten_tomatoes`.

## Behavior and storage

- Downloads are streamed one at a time; each completed file is placed under `wwwroot/download/{owner}/{repo}/{config}/{split}/`.
- The `wwwroot/download` contents are ignored by Git so user data is not included in source checkpoints.
- The MVC app discovers downloads by scanning disk; there is no database. Download queue/progress state is in memory and is lost on app restart.
- Files under `wwwroot` are public static files. Do not use this app for private data or expose it to an untrusted audience without changing its access model.
- A managed hosted container's filesystem is ephemeral; local disk downloads may disappear after restart or replacement. Use a trusted self-hosted/local instance for durable files, or add durable storage in a future version.
- Remote preview depends on the Hugging Face Dataset Viewer being available for that dataset/config/split. If not, download a shard and use local preview.
- Local pagination reads only the requested Parquet row group(s), not the whole file. Scalar columns are shown in a grid; nested/repeated columns are identified but not expanded.

## Deploy container

The repository includes a multi-stage .NET 10 Dockerfile. A publication uses the committed container contract and `/_app/health`; it does not make `wwwroot/download` durable.
