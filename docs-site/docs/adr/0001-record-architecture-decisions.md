---
title: ADR-0001 — Record architecture decisions
sidebar_position: 3
description: Quyết định ghi lại mọi quyết định kiến trúc dưới dạng ADR.
tags: [adr, accepted, meta]
---

# ADR-0001 — Record architecture decisions

- **Status:** <span className="badge-adr badge-adr-accepted">Accepted</span>
- **Date:** 2026-05-08
- **Deciders:** @hoanggggf
- **Tags:** `meta`, `documentation`

## Context

Dự án Hdos đang grow — nhiều quyết định kiến trúc đã chốt (Clean Architecture, JWT HS256, RabbitMQ topic exchange, tách port REST/gRPC…). Hiện các "vì sao" này nằm rải rác trong file markdown ở `docs/`, chat Slack, và đầu của vài người.

Vấn đề:

- Dev mới onboard hỏi đi hỏi lại "vì sao thế này"
- Sau 6 tháng không ai nhớ context của quyết định
- Khó refactor lớn vì sợ phá assumption không document

## Decision

> **Chúng tôi sẽ ghi mọi quyết định kiến trúc significant dưới dạng ADR trong folder `docs-site/docs/adr/`.**

Format theo [Michael Nygard ADR template](https://cognitect.com/blog/2011/11/15/documenting-architecture-decisions): Context → Decision → Consequences → Alternatives.

ADR là **immutable** sau khi merge: muốn đổi → tạo ADR mới với status `Supersedes ADR-NNNN`.

"Significant" = quyết định nào mà:

- Khó/đắt để đảo ngược (database choice, framework, security model)
- Ảnh hưởng nhiều service / nhiều developer
- Có alternatives đáng kể đã cân nhắc và bị loại

## Consequences

### Positive ✅

- Onboard mới đọc 1 lần là hiểu "tại sao"
- Refactor lớn có baseline rõ — ADR cũ vẫn hiển thị cho thấy quyết định cũ
- Bắt buộc tác giả nghĩ kỹ alternatives trước khi commit

### Negative ❌

- Thêm overhead: mỗi quyết định lớn = thêm 1 PR
- Có nguy cơ ADR mỏng (template-filling) — cần code review chặn

### Neutral ⚖️

- Cần convention số thứ tự, status badge, sidebar entry

## Alternatives considered

### Alt A — Ghi chú trong commit message

- **Vì sao không chọn**: commit message khó tra cứu sau, không structured.

### Alt B — Ghi vào Confluence/Notion

- **Vì sao không chọn**: tách khỏi codebase, dễ stale, không versioned cùng PR.

### Alt C — Comment dài trong code

- **Vì sao không chọn**: comment nói "what" chứ không "why bigger picture", không thấy alternatives.

## References

- [adr.github.io](https://adr.github.io)
- [Documenting architecture decisions — Nygard](https://cognitect.com/blog/2011/11/15/documenting-architecture-decisions)
- [Template](./template)
