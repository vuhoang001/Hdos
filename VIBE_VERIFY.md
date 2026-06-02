# VIBE_VERIFY.md — Intent → Spec → Generate → Review → Iterate → Ship

> Quy trình làm việc với AI có kiểm soát. Mỗi bước có đầu vào rõ ràng, đầu ra cụ thể, và điều kiện để chuyển tiếp.

```
[1] INTENT ──► [2] SPEC ──► [3] GENERATE ──► [4] REVIEW ──► [6] SHIP
                                                  │
                                                  ▼
                                            [5] ITERATE ──► [4] REVIEW
```

---

## Bước 1 — INTENT: Ghi Lại Ý Định

**Ai làm:** Bạn (không cần Claude ở bước này)  
**Đầu ra:** Một text block theo format bên dưới  
**Sang bước 2 khi:** Bạn đọc lại và trả lời được: *"Nếu feature này xong, trông nó như thế nào?"*

### Format Intent

```
INTENT:      [Điều bạn muốn build — tiếng Việt, không cần technical]
WHY:         [Lý do business / vấn đề đang giải quyết]
SERVICE:     [AuthService | OrderService | NotificationService | M01Service | DataMatchingService | DynamicFormService]
NOT SCOPE:   [Thứ gì KHÔNG làm lần này — rất quan trọng]
```

### Ví dụ điền sẵn

```
INTENT:      Bác sĩ muốn xem lịch sử tất cả form mà họ đã submit
WHY:         Hiện tại submit xong là mất, bác sĩ không tra cứu lại được
SERVICE:     DynamicFormService
NOT SCOPE:   Không filter theo ngày, không phân trang (sprint sau làm)
```

---

## Bước 2 — SPEC: Intent → Technical Spec

**Ai làm:** Claude (bạn paste prompt bên dưới)  
**Đầu ra:** Spec document — Claude viết, bạn review và approve  
**Claude KHÔNG viết code ở bước này**  
**Sang bước 3 khi:** Bạn tick xong Spec Review Checklist

### Prompt cho Bước 2 (copy và điền INTENT vào)

```
Đây là intent của tôi:

INTENT:    [INTENT]
WHY:       [WHY]
SERVICE:   [SERVICE]
NOT SCOPE: [NOT_SCOPE]

Hãy tạo Technical Spec theo format sau. KHÔNG viết code. Tôi sẽ review spec trước.

**1. Domain Changes**
Entity / ValueObject nào cần tạo hoặc sửa? Method nào thêm vào entity?

**2. API Contract**
- Method + Route
- Request (body / param / query)
- Response DTO (shape cụ thể)
- HTTP status codes

**3. Business Rules**
Liệt kê từng rule theo dạng: "Nếu X thì Y"

**4. Validation Rules**
Field: rule (VD: NotEmpty, MaxLength(100), phải là Guid hợp lệ)

**5. Files Plan**
| Action | File path đầy đủ |
|--------|-----------------|
| CREATE | ... |
| MODIFY | ... |

**6. Test Cases**
| Scenario | Input | Expected Result |
|----------|-------|----------------|
| Happy path | ... | Result.IsSuccess == true |
| Error case 1 | ... | Error.Code == "..." |
```

### Spec Review Checklist

Trước khi approve spec:

```
[ ] Domain changes đúng service, đúng layer?
[ ] API contract đúng convention (xem CLAUDE.md Section 4 Naming)?
[ ] Tất cả business rule từ INTENT đã có trong spec?
[ ] NOT SCOPE thực sự không xuất hiện trong spec?
[ ] File plan có nằm trong đúng 4 project của service không?
[ ] Test cases cover đủ: 1 happy path + ít nhất 2 error cases?
```

---

## Bước 3 — GENERATE: Sinh Code Từ Spec

**Ai làm:** Claude (bạn dùng spec để fill vào PROMPT_TEMPLATES.md)  
**Đầu ra:** Code files theo scope đã approve  
**Sang bước 4 khi:** Claude xong và đã hiển thị Post-Session Checklist

### Cách thực hiện

1. Mở `PROMPT_TEMPLATES.md`
2. Chọn template phù hợp với loại task trong spec:
   - Feature mới → **Template 2** (Command/Query)
   - Endpoint mới → **Template 1** (Endpoint)
   - Integration giữa service → **Template 4** (Integration)
3. Thay placeholder bằng thông tin từ Spec đã approve
4. Paste vào Claude

### Gate bắt buộc (CLAUDE.md Section 9 tự động enforce)

Claude sẽ:
1. List ra scope (Files TẠO / SỬA)
2. Chờ bạn nói "OK" trước khi viết code
3. Nhắc Post-Session Checklist sau khi xong

Nếu Claude bắt đầu code mà **chưa** hỏi bạn confirm scope → nhắc: *"Đưa file plan trước đi"*

---

## Bước 4 — REVIEW: Đối Chiếu Code Với Spec

**Ai làm:** Bạn (với hỗ trợ của Claude nếu cần)  
**Đầu ra:** Danh sách issues (hoặc "sạch")  
**Sang bước 5:** Nếu có issue  
**Sang bước 6:** Nếu checklist sạch 100%

### Prompt Review (dán vào Claude sau khi Generate xong)

Xem **Template 7** trong `PROMPT_TEMPLATES.md`

### Code Review Checklist

```
--- MATCH SPEC ---
[ ] Từng business rule trong spec có được implement không?
[ ] API contract (route, method, response shape) đúng spec không?
[ ] Tất cả validation rules trong spec có trong Validator không?

--- CONVENTION (CLAUDE.md) ---
[ ] Handler trả Result<T>, không ném exception?
[ ] Entity được tạo qua static factory Create(), không dùng new?
[ ] Không có public setter trên Entity / ValueObject?
[ ] Không có business logic trong Controller hay Repository?
[ ] Mỗi Command/Query có Validator riêng?

--- BUILD & TEST ---
[ ] dotnet build — pass, không có warning mới?
[ ] dotnet test — tất cả test pass?

--- SCOPE ---
[ ] git diff --name-only — chỉ có files trong scope đã approve?
```

---

## Bước 5 — ITERATE: Sửa Issues

**Ai làm:** Claude sửa, bạn verify từng issue  
**Rule quan trọng:** Fix từng issue một — không gộp  
**Sang bước 4:** Sau mỗi fix, chạy lại Review Checklist

### Prompt Iterate (dán vào Claude kèm issue cụ thể)

Xem **Template 8** trong `PROMPT_TEMPLATES.md`

### Cách ghi issue cho rõ

```
Issue #1: [MÔ TẢ — VD: "Handler gọi new Order() thay vì Order.Create()"]
File:     [ĐƯỜNG DẪN FILE]
Line:     [SỐ DÒNG NẾU BIẾT]
Fix:      Dùng factory method Order.Create(customerId, items)
```

---

## Bước 6 — SHIP: Commit Sạch, Push, Done

**Ai làm:** Bạn chạy `scripts/ship-check.sh`, sau đó commit  
**Đầu ra:** Commit có message tốt, docs cập nhật  
**Sang bước tiếp theo:** Feature mới trong backlog

### Ship Checklist

```
[ ] scripts/ship-check.sh pass (build + test tự động)
[ ] git diff --name-only — chỉ files đúng scope
[ ] Docs: đã tạo docs/NN-*.md? (feature mới bắt buộc)
[ ] Contracts: đã thêm IntegrationEvent mới vào Contracts project?
[ ] Không có connection string / secret trong code
[ ] Commit message có WHY, không chỉ WHAT
```

### Format Commit Message

```
feat(service-name): [mô tả ngắn gọn]

Giải quyết: [vấn đề business từ INTENT]
Approach: [lý do chọn cách này nếu có trade-off]
Not in scope: [thứ chủ động bỏ qua — tham chiếu từ NOT SCOPE]
```

---

## Cheatsheet — Dùng Hàng Ngày

| Bước | Việc bạn làm | Việc Claude làm |
|------|-------------|----------------|
| INTENT | Điền 4 field (2 phút) | Không làm gì |
| SPEC | Paste prompt | Tạo spec, không code |
| GENERATE | Fill template, approve scope | Viết code |
| REVIEW | Tick checklist | Hỗ trợ review theo Template 7 |
| ITERATE | Liệt kê issues | Fix từng issue |
| SHIP | Chạy ship-check.sh, commit | Gợi ý commit message |

**Thời gian trung bình một feature nhỏ:** INTENT (5 phút) + SPEC (10 phút review) + GENERATE (tự động) + REVIEW (15 phút) + SHIP (5 phút) = ~35 phút kiểm soát được.
