using Hdos.DynamicFormService.Application.DTOs;
using Hdos.SharedKernel;
using MediatR;

namespace Hdos.DynamicFormService.Application.Features.WidgetCatalog.GetWidgetCatalog;

public sealed record GetWidgetCatalogQuery : IRequest<Result<List<WidgetCatalogItemDto>>>;

public sealed class GetWidgetCatalogQueryHandler : IRequestHandler<GetWidgetCatalogQuery, Result<List<WidgetCatalogItemDto>>>
{
    private static readonly List<WidgetCatalogItemDto> Catalog =
    [
        new("FormSection",        "Form",                 "Nhúng một form động vào vị trí này trên màn hình", 6, 8),
        new("TextBlock",          "Khối văn bản",         "Tiêu đề hoặc đoạn văn bản hướng dẫn",             6, 2),
        new("Divider",            "Đường phân cách",      "Đường ngang chia tách các phần",                   12, 1),
        new("ImageBlock",         "Ảnh",                  "Hiển thị một hình ảnh tĩnh",                       4, 4),
        new("ConditionalSection", "Phần điều kiện",       "Container ẩn/hiện theo giá trị form field",        6, 6)
    ];

    public Task<Result<List<WidgetCatalogItemDto>>> Handle(GetWidgetCatalogQuery request, CancellationToken ct)
        => Task.FromResult(Result.Success(Catalog));
}
