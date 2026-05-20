# 🚀 MiniSmtpServer

> [!WARNING]
> This project is currently highly experimental and **not stable for parallel or concurrent connections**.  
> Unexpected behavior, race conditions, or connection issues may occur under load.  
> It should currently be considered a proof of concept / experiment and **must not be used in production environments**.


**A lightweight, ultra-fast SMTP server built with .NET, designed for maximum performance and minimal latency.**

MiniSmtpServer is a high-performance SMTP implementation focused on speed, simplicity, and efficiency.  
The goal of this project is to build an **extremely fast SMTP server capable of handling connections with very low latency (<5ms in local networks)** while maintaining a minimal and predictable architecture.

---

## 🎯 Goals

- ⚡ Ultra-low latency SMTP processing
- 🚀 High-throughput connection handling
- 🧠 Minimal overhead architecture (no unnecessary abstractions)
- 📦 Lightweight and container-friendly design
- 🔧 Easy to run, test, and benchmark
- 🧪 Benchmarking against tools like Mailpit, Postfix, and other SMTP servers
- 🔬 Experimentation with high-performance networking in .NET

---

## 🏗️ Design Philosophy

This project focuses on:

- Simple and predictable execution flow
- Low allocation / low GC pressure design
- Efficient socket handling strategies
- Avoiding unnecessary async complexity where it hurts performance
- OS-level performance awareness (TCP, buffering, scheduling)
- Practical, measurable performance improvements

---

## 📊 Use Cases

- SMTP performance benchmarking
- Mail pipeline testing
- Development mail catcher replacement
- Research into high-performance .NET networking
- Lightweight email ingestion services

---

## ⚠️ Status

This project is currently **experimental**.  
APIs, architecture, and internal implementation may change frequently as performance optimizations are introduced.

---

## Version Comparison & Optimization History

The SMTP server evolved through multiple iterations with a strong focus on reducing latency, minimizing allocations, simplifying parsing logic, and improving throughput under SMTP PIPELINING workloads.

### High-Level Blueprint

| Version | Architecture Style | Core Innovation / Strategy |
| :--- | :--- | :--- |
| **V1** | Async-per-connection | Baseline implementation via standard `StreamReader`/`Writer`. |
| **V5** | Reactor Model | Non-blocking multi-connection loop using `Socket.Select`. |
| **V6** | Reactor + Pooling | Introduced `ArrayPool` and pre-encoded responses to drop allocations. |
| **V9** | Task-based Async I/O | Standard .NET async pipeline using `NetworkStream` (Simplicity focus). |
| **V10** | Manual Zero-Alloc Attempt | Dropped `StreamReader`. Manual buffer splicing and custom state machine. |
| **V11** | High-Perf Async Sockets | Native `SocketAsyncEventArgs` + UTF-8 literals (`"EHLO"u8`). |
| **V12** | System.IO.Pipelines | **Ultimate Leap:** Zero-copy parsing via `SequenceReader<byte>` + Batch Flushing. |

### Impact per Version (Latency / GC / Throughput)

> Note: Measurements are based on a single-client sequential SMTP workload (mails sent one after another over ~20ms RTT connection with PIPELINING enabled).  
> This primarily reflects **per-mail processing efficiency**, not high-concurrency scaling behavior.

| Version | Latency Impact | GC Impact | Throughput Impact | Summary |
| :--- | :--- | :--- | :--- | :--- |
| **V1** | Baseline (140 ms) | High (StreamReader + string parsing) | Low | Simple baseline, heavy abstraction overhead |
| **V5** | ↓ ~28–30% (100 ms) | Medium (no string-heavy Streams per connection) | Medium | Reactor model removes thread/Task overhead but still parsing-heavy |
| **V6** | Stable (100 ms) | ↓ Significant (ArrayPool + pre-encoded responses) | Medium | Biggest gain: allocation reduction, GC pressure drops clearly |
| **V9** | Stable (100 ms) | Medium-High (StreamReader reintroduced) | Medium | Simpler design, but loses low-level efficiency gains |
| **V10** | Stable / slight ↓ | ↓ Medium (manual parsing + pooled buffers) | Medium-High | First real “zero-alloc direction”, better control over hot path |
| **V11** | Stable / slight ↓ | ↓↓ (UTF-8 literals, fewer encodings, better socket usage) | High | Hot-path optimized async sockets, reduced CPU per command |
| **V12** | ↓↓↓ (70 ms) | ↓↓↓ (near-zero allocations in hot path) | Very High | Pipelines + zero-copy + batching = best overall efficiency |


### Results

| Version | Latency (ms) |
|--------|-------------:|
| V1     | 140 ms       |
| V5     | 100 ms       |
| V6     | 100 ms       |
| V9     | 100 ms       |
| V10    | 100 ms       |
| V11    | 100 ms       |
| V12    | 70 ms        |

The optimization work in V12 reduced the average processing time by approximately **50% compared to V1** under identical benchmark conditions.

## 🔍 Key Optimization Phases

### Phase 1: Architectural Shifts & Concurrency (V1 → V5 → V9)
* **The Change:** Moving from standard thread-per-connection (`Task.Run`) to a single-threaded Reactor loop (`Socket.Select`), and later refactoring to structured `AcceptAsync`.
* **The Impact:** Drastically reduced CPU context switching and thread overhead. V9 re-introduced readable async code but exposed the allocation costs of high-level streams.

### Phase 2: Eliminating Allocations & Strings (V6 → V10 → V11)
* **The Change:** Replacing `Encoding.UTF8.GetBytes` and string parsing with native **UTF-8 literals (`"DATA"u8`)**, SIMD-friendly prefix checks, and recycling memory via `ArrayPool<byte>`.
* **The Impact:** Eliminated per-request allocations in the hot path. Latency became highly predictable as Garbage Collection (GC) spikes dropped to near zero.

### Phase 3: The System.IO.Pipelines Breakthrough (V12)
* **The Change:** Full transition to `PipeReader` and `PipeWriter`. Parsing is done directly on the network buffers via `SequenceReader<byte>`.
* **The Innovations:** * **Zero-Copy:** No interim arrays or strings are created during parsing.
    * **Smart Batching:** Multiple SMTP responses are accumulated and flushed in a single syscall.
    * **Hardware Scanning:** Vectorized boundary detection for the final data termination (`\r\n.\r\n`).

---


### Overall Result

| Metric | V1 | V12 |
|---|---|---|
| Average Processing Time | 140 ms | 70 ms |
| Relative Improvement | Baseline | ~50% faster |

The benchmark results demonstrate that most gains came from cumulative low-level optimizations rather than a single architectural change.

---

## Testing

Each server version can be tested independently by passing the version number as a runtime argument.  
This allows direct A/B comparison of architectural changes, performance improvements, and memory behavior across all iterations.

All images are published under a unified container registry tag, where the version number selects the implementation (V1 → V12).

### Run a specific version

```bash
docker run -p 25:25 ghcr.io/tinohager/mini-smtp-server:latest 1

# V1 - Baseline (StreamReader / async-per-connection)
docker run -p 25:25 ghcr.io/tinohager/mini-smtp-server:latest 1

# V5 - Reactor model (Socket.Select event loop)
docker run -p 25:25 ghcr.io/tinohager/mini-smtp-server:latest 5

# V6 - Reactor + pooling + reduced allocations
docker run -p 25:25 ghcr.io/tinohager/mini-smtp-server:latest 6

# V9 - Task-based async I/O (NetworkStream abstraction)
docker run -p 25:25 ghcr.io/tinohager/mini-smtp-server:latest 9

# V10 - Manual zero-allocation parsing (state machine)
docker run -p 25:25 ghcr.io/tinohager/mini-smtp-server:latest 10

# V11 - High-performance sockets + UTF-8 literals
docker run -p 25:25 ghcr.io/tinohager/mini-smtp-server:latest 11

# V12 - System.IO.Pipelines (zero-copy + batching)
docker run -p 25:25 ghcr.io/tinohager/mini-smtp-server:latest 12
```

#### Better SMTP PIPELINING Efficiency
The server was optimized specifically for SMTP PIPELINING scenarios where multiple commands arrive in a single network packet.

Typical pipelined flow:

```text
MAIL FROM
RCPT TO
DATA
```

Processing these commands efficiently without unnecessary synchronization or parsing overhead had a major impact on throughput.

Typical SMTP communication:

| Mode | Estimated Roundtrips |
|---|---|
| Standard SMTP | ~5–6 RTT |
| SMTP PIPELINING | ~2–3 RTT |

Because the benchmarks were executed with PIPELINING enabled, the measured timings primarily reflect the internal processing performance of the SMTP server implementation rather than network latency overhead.

