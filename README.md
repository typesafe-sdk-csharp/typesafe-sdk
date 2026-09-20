# TypeSafe .NET SDK (TypeSafe.AI)

[![CI](https://github.com/typesafe-sdk-csharp/typesafe-sdk/actions/workflows/ci.yml/badge.svg)](https://github.com/typesafe-sdk-csharp/typesafe-sdk/actions/workflows/ci.yml)
[![Release & Publish](https://github.com/typesafe-sdk-csharp/typesafe-sdk/actions/workflows/release.yml/badge.svg)](https://github.com/typesafe-sdk-csharp/typesafe-sdk/actions/workflows/release.yml)
[![NuGet Version](https://img.shields.io/nuget/v/TypeSafe.AI.svg?style=flat&logo=nuget)](https://www.nuget.org/packages/TypeSafe.AI)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)

Unofficial, enterprise-ready **.NET SDK for TypeSafe AI** — providing high-throughput verification, deterministic question building, and evaluation for LLMs and AI agent workflows.

---

## ✨ Features

- **🚀 NativeAOT & Trimming Compatible** — Zero-reflection architecture with `System.Text.Json` source generation for blazing startup performance and minimal footprint.
- **🛡️ Type-Safe Verification** — Build structured questionnaires (`Questions.Build`) with support for **Noul** (yes/no), **Choice** (multiple choice), and **Score** (rubric) question types.
- **⚡ Enterprise Resilience** — Built-in HTTP resilience powered by `Microsoft.Extensions.Http.Resilience` (Polly v8) with automated retries, rate-limit backoff, per-attempt timeouts, and total deadline budgets.
- **📊 Observability & OpenTelemetry** — Native `ActivitySource` tracing emitting model, token usage, request IDs, and structured diagnostics.
- **🧩 Idiomatic .NET** — Seamless integration with `Microsoft.Extensions.DependencyInjection`, `IConfiguration`, and the `IHttpClientFactory` ecosystem.
- **🔒 Structured Error Handling** — Typed exception hierarchy (`TypeSafeAuthenticationException`, `TypeSafeRateLimitException`, `TypeSafeTimeoutException`, etc.) with request IDs, response bodies, and `Retry-After` headers.

---

## 📦 Installation

### Stable Releases (NuGet.org)

```bash
dotnet add package TypeSafe.AI
```

### Preview / Beta Builds (GitHub Packages)

Preview builds are published on every pull request via GitHub Packages. To consume beta packages:

1. Add a `nuget.config` to your repository root (see [`nuget.config.example`](nuget.config.example)):

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="github" value="https://nuget.pkg.github.com/typesafe-sdk-csharp/index.json" />
  </packageSources>
  <packageSourceCredentials>
    <github>
      <add key="Username" value="YOUR_GITHUB_USERNAME" />
      <add key="ClearTextPassword" value="%GITHUB_TOKEN%" />
    </github>
  </packageSourceCredentials>
</configuration>
```

2. Install with the `--prerelease` flag:

```bash
dotnet add package TypeSafe.AI --prerelease
```

---

## 🚀 Quickstart

### 1. Register the Client via Dependency Injection

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TypeSafe.AI;

var builder = Host.CreateApplicationBuilder(args);

// Option A: Configure with a callback
builder.Services.AddTypeSafeClient(options =>
{
    options.ApiKey = Environment.GetEnvironmentVariable("TYPESAFE_API_KEY");
});

// Option B: Bind from IConfiguration (reads the "TypeSafe" section)
builder.Services.AddTypeSafeClient(builder.Configuration);

var host = builder.Build();
var client = host.Services.GetRequiredService<ITypeSafeClient>();
```

### 2. Build Questions & Evaluate

```csharp
// Build a verification questionnaire with all three question types
var questions = Questions.Build(q => q
    .Noul("refund", "Is the customer asking for money back?")
    .Choice("team", "Which team should handle this?", "billing", "support")
    .Score("urgency", "How urgent is this?", "low", "medium", "high"));

// Execute evaluation against TypeSafe System One
var response = await client.SystemOneAsync(
    "I was charged twice for my subscription and need this reversed immediately!",
    questions);

// Access strongly-typed responses
Console.WriteLine($"Refund probability: {response.Nouls["refund"].Noul:P0}");
Console.WriteLine($"Team: {response.Choices["team"].Choice}");
Console.WriteLine($"Urgency score: {response.Scores["urgency"].Score:F2}");
```

---

## 📖 API Reference

### Question Types

The SDK supports three question primitives, all built fluently through `Questions.Build`:

#### Noul (Yes/No)

Returns a probability from `0.0` (no) to `1.0` (yes), with `~0.5` indicating uncertainty.

```csharp
// Simple — instruction only
q.Noul("billing", "Is this ticket about billing?")

// With outcome criteria
q.Noul("billing", "Is this about billing?",
    whenTrue: "Customer mentions charges, invoices, or payments",
    whenFalse: "Customer discusses product features or bugs")

// With structured content
q.Noul("billing", TypeSafeContent.FromString("Is this about billing?"),
    new NoulCriteria
    {
        WhenTrue = TypeSafeContent.FromString("Mentions charges or payments"),
        WhenFalse = TypeSafeContent.FromString("Discusses features or bugs")
    })
```

**Response**: `NoulAnswer` — access via `response.Nouls["id"].Noul`

---

#### Choice (Multiple Choice)

Selects one option from a set of named alternatives and returns the full probability distribution.

```csharp
// Simple labels
q.Choice("tone", "What is the customer's tone?", "calm", "frustrated", "angry")

// With described options (sub-builder)
q.Choice("tone", "What is the customer's tone?", opts => opts
    .Option("calm", "Polite and patient language")
    .Option("frustrated", "Shows signs of dissatisfaction")
    .Option("angry", "Aggressive or threatening language"))
```

**Response**: `ChoiceAnswer` — access via `response.Choices["id"]`
- `.Choice` — the top-selected label
- `.Probabilities` — `IReadOnlyDictionary<string, double>` of all options
- `.Confidence` — confidence score from `0.0` to `1.0`

---

#### Score (Rubric)

Assigns a probability-weighted score along an ordered rubric (2–10 levels). The score may fall between levels.

```csharp
// Simple text levels
q.Score("urgency", "How urgent is this ticket?", "can wait", "this week", "today")

// With described levels (sub-builder)
q.Score("urgency", "How urgent is this?", levels => levels
    .Level("Low — no immediate action needed")
    .Level("Medium — should be addressed this week")
    .Level("High — requires same-day resolution"))
```

**Response**: `ScoreAnswer` — access via `response.Scores["id"]`
- `.Score` — the weighted score value
- `.Legend` — `IReadOnlyDictionary<string, string>` mapping index to level label
- `.Probabilities` — `IReadOnlyDictionary<string, double>` across levels
- `.Confidence` — confidence score from `0.0` to `1.0`

---

### Structured Content

State and instructions accept `TypeSafeContent` — plain text, JSON objects, or JSON arrays:

```csharp
// Plain text (also implicitly converted from string)
TypeSafeContent.FromString("Hello, world!")

// JSON node (deep-cloned for immutability)
TypeSafeContent.FromJson(new JsonObject
{
    ["role"] = "user",
    ["message"] = "I need a refund"
})

// Strongly-typed object via source-generated serializer
TypeSafeContent.FromObject(myTicket, MyJsonContext.Default.SupportTicket)
```

---

### Direct Client Construction

For standalone scenarios outside DI (e.g., console apps, scripts, tests), use `TypeSafeClient.Create` which manages and disposes its own `HttpClient`:

```csharp
var options = TypeSafeClientOptions.FromEnvironment(); // reads TYPESAFE_API_KEY, etc.
using var client = TypeSafeClient.Create(options);
```

Or bring your own `HttpClient` if you manage connection lifetimes externally:

```csharp
using var httpClient = new HttpClient();
using var client = new TypeSafeClient(httpClient, options);
```

> **Note**: When using `TypeSafeClient.Create()`, the client owns and disposes the underlying `HttpClient`. When passing an existing `HttpClient`, the caller retains ownership of the transport. In both standalone patterns, retry and per-attempt timeout handlers are **not** installed — the DI-registered client (`AddTypeSafeClient`) configures the full resilience pipeline automatically.

---

## ⚙️ Configuration

### Via `appsettings.json`

```json
{
  "TypeSafe": {
    "ApiKey": "YOUR_API_KEY",
    "BaseUrl": "https://api.typesafe.ai/",
    "DefaultModel": "jev-latest",
    "AllowInsecureHttp": false,
    "Retry": {
      "MaxRetries": 2,
      "BackoffInitial": "00:00:00.500",
      "BackoffMax": "00:00:05",
      "UseJitter": true,
      "RespectRetryAfter": true,
      "PerAttemptTimeout": "00:00:10",
      "TotalTimeoutBudget": "00:00:30"
    }
  }
}
```

### Via Environment Variables

| Variable | Default | Description |
|---|---|---|
| `TYPESAFE_API_KEY` | — | API key (**required**) |
| `TYPESAFE_BASE_URL` | `https://api.typesafe.ai/` | API root URL (must end with `/`) |
| `TYPESAFE_DEFAULT_MODEL` | `jev-latest` | Model used when not overridden per-call |

### Configuration Precedence

Options are applied in this order (last wins):

1. **Defaults** → built-in values
2. **Environment variables** → `ApplyEnvironmentVariables()`
3. **`IConfiguration` section** → `"TypeSafe"` section from `appsettings.json`
4. **Callback** → `Action<TypeSafeClientOptions>` passed to `AddTypeSafeClient`

### `TypeSafeRetryOptions` Reference

| Property | Type | Default | Description |
|---|---|---|---|
| `MaxRetries` | `int` | `2` | Retry attempts after the initial request (0–24). Zero disables retries. |
| `BackoffInitial` | `TimeSpan` | `500ms` | Initial exponential-backoff delay. |
| `BackoffMax` | `TimeSpan` | `5s` | Cap on generated backoff delays (Retry-After values are not capped). |
| `UseJitter` | `bool` | `true` | Adds randomness to backoff delays. Disable only in deterministic tests. |
| `RespectRetryAfter` | `bool` | `true` | Honors `Retry-After` response headers from the server. |
| `PerAttemptTimeout` | `TimeSpan?` | `10s` | Bounds each HTTP attempt. `null` disables. |
| `TotalTimeoutBudget` | `TimeSpan?` | `30s` | Overall deadline including retries and body reads. `null` disables the SDK deadline. |

---

## 🚨 Error Handling

All SDK exceptions inherit from `TypeSafeException`, which exposes:

| Property | Type | Description |
|---|---|---|
| `StatusCode` | `HttpStatusCode?` | The HTTP status code, if applicable |
| `RequestId` | `string?` | The `x-typesafe-request-id` header value |
| `ResponseBody` | `string?` | The raw response body (never included in `.Message` to prevent log leakage) |
| `RetryAfter` | `string?` | The `Retry-After` header value, when present |

### Exception Hierarchy

| Exception | HTTP Status | When |
|---|---|---|
| `TypeSafeAuthenticationException` | `401` | Invalid or missing API key |
| `TypeSafeValidationException` | `422` | Malformed request rejected by the API |
| `TypeSafeRateLimitException` | `429` | Rate limit exceeded |
| `TypeSafeOverloadedException` | `529` | Service temporarily overloaded |
| `TypeSafeTimeoutException` | — | Per-attempt or total deadline exceeded |
| `TypeSafeConnectionException` | — | HTTP transport or I/O failure |
| `TypeSafeProtocolException` | — | Unexpected JSON structure in the response |

```csharp
try
{
    var response = await client.SystemOneAsync(state, questions);
}
catch (TypeSafeRateLimitException ex)
{
    Console.WriteLine($"Rate limited. Retry after: {ex.RetryAfter}");
}
catch (TypeSafeTimeoutException ex)
{
    Console.WriteLine($"Request timed out: {ex.Message}");
}
catch (TypeSafeException ex)
{
    Console.WriteLine($"TypeSafe error [{ex.StatusCode}]: {ex.Message}");
    Console.WriteLine($"Request ID: {ex.RequestId}");
}
```

---

## 📊 Observability

The SDK emits OpenTelemetry-compatible traces via `System.Diagnostics.ActivitySource`:

- **Source name**: `TypeSafe.AI`
- **Activity name**: `TypeSafe.SystemOne`

### Emitted Tags

| Tag | Description |
|---|---|
| `typesafe.model` | Model used for evaluation |
| `typesafe.usage.input_tokens` | Input token count (when reported) |
| `typesafe.usage.output_tokens` | Output token count (when reported) |
| `typesafe.request_id` | Server-assigned request identifier |

```csharp
// Wire up in your OpenTelemetry configuration
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource("TypeSafe.AI"));
```

---

## 📂 Repository Structure

```
project-typesafe-csharp-sdk/
├── .github/
│   └── workflows/
│       ├── ci.yml                 # Build, test, and pack on PR / main
│       └── release.yml            # Release-please automated versioning & publishing
├── src/
│   └── TypeSafe.AI/               # Core SDK library (TypeSafe.AI NuGet package)
├── samples/
│   └── TypeSafe.Sample.HelloJav/  # Getting started console sample
├── tests/
│   └── TypeSafe.AI.Tests/         # Unit & HTTP-handler regression tests
├── Directory.Build.props          # Global compiler, AOT, and packaging settings
├── Directory.Packages.props       # Central Package Management (CPM)
├── global.json                    # Pinned .NET 10 SDK version
├── nuget.config.example           # Example config for GitHub Packages
└── TypeSafe.slnx                  # Solution file
```

---

## 🛠️ Building & Testing Locally

**Requirements**: [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (pinned via `global.json`)

```bash
# Clone the repository
git clone https://github.com/typesafe-sdk-csharp/typesafe-sdk.git
cd typesafe-sdk

# Restore dependencies
dotnet restore

# Build solution
dotnet build --configuration Release --no-restore

# Run tests (in-memory HTTP handlers, no API calls)
dotnet test --configuration Release --no-build

# Pack NuGet package
dotnet pack --configuration Release --no-build --output artifacts/packages

# Run sample (requires TYPESAFE_API_KEY)
dotnet run --project samples/TypeSafe.Sample.HelloJav/TypeSafe.Sample.HelloJav.csproj
```

---

## 🔄 CI/CD & Automated Releases

This repository uses automated continuous integration and trunk-based releases:

- **Continuous Integration (`ci.yml`)** — Restores, builds in Release mode, runs all tests, and validates NuGet packing on every PR and commit to `main`.
- **Release Please (`release.yml`)** — Tracks [Conventional Commits](https://www.conventionalcommits.org/) (`feat:`, `fix:`) to automatically maintain `CHANGELOG.md` and bump versions via automated Release PRs.
- **Beta Channel** — Preview packages are published to GitHub Packages upon opening/updating a Release PR.
- **General Availability (GA)** — Once a Release PR is merged into `main`, production `.nupkg` and `.snupkg` packages are pushed to [NuGet.org](https://www.nuget.org/packages/TypeSafe.AI).

---

## 📄 License

This project is licensed under the [MIT License](LICENSE).
