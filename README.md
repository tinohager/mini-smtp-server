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
docker run -p 25:25 ghcr.io/tinohager/mini-smtp-server:latest
```
