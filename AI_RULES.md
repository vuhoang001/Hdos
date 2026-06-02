# AI_RULES.md — Quy Trình Làm Việc Với AI (Kiểm Soát Được)

> File này là protocol cá nhân — quy định **cách bạn** làm việc với Claude Code.  
> `PROMPT_TEMPLATES.md` là công cụ viết prompt. File này là quy trình bao quanh việc đó.

---

## Rule 0 — Trước Khi Bắt Đầu Bất Kỳ Task Nào

Trả lời đủ 3 câu này. Nếu không trả lời được → chưa sẵn sàng giao AI.

```
WHAT:        Cụ thể cần làm gì? (1 câu, có tên file/feature/entity)
CONSTRAINT:  Không được làm gì? (file nào không được sửa, pattern nào không dùng)
DONE WHEN:   Khi nào task xong? (file nào phải có, test nào phải pass)
```

**Ví dụ xấu:** *"Thêm tính năng delete vào DynamicFormService"*  
**Ví dụ tốt:**
```
WHAT:        Thêm DeleteFormTemplate command vào DynamicFormService
CONSTRAINT:  Không sửa FormModule hay FormField — chỉ xử lý FormTemplate
DONE WHEN:   Có Command + Handler + Validator. Build pass. Không cần migration.
```

---

## Rule 1 — 4 Phase Bắt Buộc Trong Mỗi Session

Không được nhảy cóc phase. Mỗi phase phải kết thúc rõ ràng trước khi sang phase tiếp theo.

```
Phase 1 — EXPLORE   AI đọc code liên quan, bạn verify nó hiểu đúng context chưa
Phase 2 — PLAN      AI đề xuất danh sách file sẽ tạo/sửa, bạn approve hoặc điều chỉnh
Phase 3 — EXECUTE   AI viết code theo plan đã approve
Phase 4 — REVIEW    Bạn đọc diff, chạy build + test, kiểm tra scope
```

**Dấu hiệu Phase 1 đã xong:** AI có thể mô tả đúng pattern đang dùng trong codebase mà không phỏng đoán.  
**Dấu hiệu Phase 2 đã xong:** Bạn đã xác nhận "OK, làm đi" với danh sách file cụ thể.

---

## Rule 2 — Khai Báo Scope Trước Khi Execute

Trước khi AI bắt đầu viết code, phải có danh sách file rõ ràng:

```
Files sẽ TẠO MỚI:
- src/Services/XxxService/XxxService.Application/Features/Yyy/YyyCommand.cs
- src/Services/XxxService/XxxService.Application/Features/Yyy/YyyCommandHandler.cs
- src/Services/XxxService/XxxService.Application/Features/Yyy/YyyCommandValidator.cs

Files sẽ SỬA:
- src/Services/XxxService/XxxService.API/Controllers/XxxController.cs

Files KHÔNG ĐƯỢC SỬA (dù AI nghĩ nên sửa):
- [liệt kê nếu cần bảo vệ]
```

Sau khi execute xong: `git diff --name-only` để verify AI không lén sửa ngoài scope.

---

## Rule 3 — Giới Hạn Tín Nhiệm Theo Loại Task

| Loại task | Mức tín nhiệm | Hành động |
|-----------|---------------|-----------|
| Boilerplate (command/query/validator mới) | Cao | Review nhanh, focus vào business logic |
| Bug fix | Trung bình | AI giải thích root cause trước, bạn verify lý luận TRƯỚC KHI cho execute |
| Kiến trúc / refactor lớn | Thấp | Thảo luận trước, AI chỉ execute từng bước nhỏ |
| Sửa CI/CD, docker-compose, Program.cs | Rất thấp | Đọc kỹ từng dòng diff trước khi chấp nhận |
| Xóa code / xóa file | Nguy hiểm | Luôn hỏi "tại sao xóa cái này?" trước |

---

## Rule 4 — Checklist Sau Mỗi Session

Tick hết trước khi đóng tab:

```
[ ] git diff --name-only — file thay đổi có nằm trong scope đã khai báo không?
[ ] dotnet build — build pass không có warning mới?
[ ] dotnet test — test hiện có vẫn pass?
[ ] Có feature mới → đã tạo docs/NN-*.md chưa? (xem docs/ để lấy số tiếp theo)
[ ] Có integration event mới → đã thêm vào Contracts project chưa?
[ ] Không có connection string hay secret trong code?
```

---

## Rule 5 — Khi Nào Phải Dừng Và Hỏi Lại

Dừng session ngay nếu AI làm bất kỳ điều nào sau:

- **Sửa file ngoài scope đã khai báo** — hỏi tại sao trước khi accept
- **Đề xuất sửa hơn 3 file cùng lúc** mà chưa được approve — từ chối, yêu cầu chia nhỏ
- **Tự tạo pattern mới** không có trong codebase (VD: tự dùng Minimal API khi codebase dùng Controller) — rollback
- **Ném exception thay vì trả `Result.Failure`** — sửa ngay, không merge
- **Dùng `new Entity(...)` thay vì factory method** — sửa ngay
- **Tự xóa code cũ** mà bạn không yêu cầu — investigate trước khi chấp nhận

---

## Rule 6 — Phân Loại Câu Hỏi Cho AI

Dùng đúng loại câu hỏi cho đúng mục đích:

| Mục đích | Cách hỏi |
|----------|----------|
| Tìm hiểu codebase | *"Đọc file X và mô tả cho tôi pattern đang dùng"* |
| Lên plan | *"Tôi muốn làm Y. Đề xuất các file cần tạo/sửa, KHÔNG viết code trước"* |
| Execute | Dùng template từ `PROMPT_TEMPLATES.md` |
| Debug | *"Đây là stack trace. Giải thích root cause. Chưa cần sửa"* |
| Review | *"Review diff này và chỉ ra điểm nào vi phạm CLAUDE.md"* |

Không gộp nhiều mục đích vào một câu hỏi.

---

## Rule 7 — Quản Lý Context Của AI

AI không nhớ session trước. Đầu mỗi session:

```
1. Nếu task liên quan đến file cụ thể → yêu cầu AI đọc file đó trước
2. Nếu task tiếp nối từ session trước → tóm tắt ngắn: "Session trước đã làm X, giờ cần Y"
3. Nếu AI bắt đầu nói sai về codebase → dừng, yêu cầu đọc lại file nguồn
```

Dấu hiệu AI bị "context drift" (đang hallucinate về codebase của bạn):
- Đề xuất pattern không tồn tại trong project
- Nhắc đến method/class không có trong file vừa đọc
- Mô tả behavior khác với code thực tế

---

## Rule 8 — Ghi Lại Quyết Định Quan Trọng

Những quyết định kiến trúc không hiển nhiên → ghi vào commit message hoặc docs, không chỉ trong chat.

Template commit message cho quyết định có trade-off:

```
feat(xxx): [mô tả ngắn]

Chọn [approach A] thay vì [approach B] vì [lý do cụ thể].
[Approach B] bị loại vì [ràng buộc gì].
```

---

## Anti-Patterns Cần Tránh

```
✗ "Làm cho tôi feature X" — quá mơ hồ, AI tự quyết định scope
✗ Approve diff mà không đọc — bạn mất kiểm soát
✗ Để AI refactor code xung quanh khi chỉ yêu cầu fix bug
✗ Dùng AI để quyết định kiến trúc — AI đề xuất, bạn quyết định
✗ Không chạy test sau session — bug tích lũy qua nhiều session
✗ Gộp nhiều task vào một session dài — mất track được AI làm gì
```

---

## Quick Reference

```
Trước task:    WHAT + CONSTRAINT + DONE WHEN
Trong session: EXPLORE → PLAN → EXECUTE → REVIEW
Sau session:   git diff + build + test + docs
Khi nghi ngờ: Dừng, đọc file gốc, verify thủ công
```

**Tham khảo thêm:**
- `PROMPT_TEMPLATES.md` — template cho từng loại task cụ thể
- `CLAUDE.md` — convention và anti-pattern của codebase
- `docs/` — tài liệu kỹ thuật từng feature
