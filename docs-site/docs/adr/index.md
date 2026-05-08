---
title: ADR Index
slug: /adr/
sidebar_position: 1
description: Toàn bộ Architecture Decision Records của Hdos.
tags: [adr, index]
---

# ADR — Architecture Decision Records

> ADR là **bản ghi ngắn** ghi lại 1 quyết định kiến trúc + bối cảnh + hệ quả. Bất biến sau khi merge — sửa = tạo ADR mới superseded.

## Trạng thái

- <span className="badge-adr badge-adr-proposed">Proposed</span> — đang đề xuất, chưa apply
- <span className="badge-adr badge-adr-accepted">Accepted</span> — đã apply, đang dùng
- <span className="badge-adr badge-adr-deprecated">Deprecated</span> — không dùng nữa nhưng chưa thay
- <span className="badge-adr badge-adr-superseded">Superseded</span> — đã bị ADR mới thay (link tới ADR thay)

## Index

| # | Title | Status | Date |
|---|---|---|---|
| [0001](./0001-record-architecture-decisions) | Record architecture decisions | <span className="badge-adr badge-adr-accepted">Accepted</span> | 2026-05-08 |
| [0002](./0002-split-rest-grpc-ports) | Tách port REST và gRPC trong cùng service | <span className="badge-adr badge-adr-accepted">Accepted</span> | 2026-05-08 |

## Cách thêm ADR mới

1. Copy [`template.md`](./template) → `NNNN-tieu-de-ngan.md` (NNNN = số kế tiếp).
2. Điền sections: Context / Decision / Consequences / Alternatives.
3. Set `status: Proposed`, mở PR.
4. Khi merge → đổi status `Accepted`, bổ sung row vào table trên.
5. Reference từ doc/code khi liên quan.

## Tham khảo

- [adr.github.io](https://adr.github.io)
- Michael Nygard's [original ADR post](https://cognitect.com/blog/2011/11/15/documenting-architecture-decisions)
