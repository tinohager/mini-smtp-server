# 🚀 MiniSmtpServer

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

## Testing

```bash
docker run -p 25:25 ghcr.io/tinohager/mini-smtp-server:latest 1

docker run -p 25:25 ghcr.io/tinohager/mini-smtp-server:latest 5

docker run -p 25:25 ghcr.io/tinohager/mini-smtp-server:latest 6

docker run -p 25:25 ghcr.io/tinohager/mini-smtp-server:latest 9

docker run -p 25:25 ghcr.io/tinohager/mini-smtp-server:latest 10
docker run -p 25:25 ghcr.io/tinohager/mini-smtp-server:latest 11
docker run -p 25:25 ghcr.io/tinohager/mini-smtp-server:latest 12
```

## 📊 Performance Benchmarks

All benchmark measurements were performed using SMTP PIPELINING with a network latency of approximately **20 ms RTT (ping)**.

SMTP PIPELINING significantly reduces the number of required network roundtrips by allowing multiple SMTP commands to be transmitted without waiting for individual server responses.

Typical SMTP communication:

| Mode | Estimated Roundtrips |
|---|---|
| Standard SMTP | ~5–6 RTT |
| SMTP PIPELINING | ~2–3 RTT |

Because the benchmarks were executed with PIPELINING enabled, the measured timings primarily reflect the internal processing performance of the SMTP server implementation rather than network latency overhead.

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
