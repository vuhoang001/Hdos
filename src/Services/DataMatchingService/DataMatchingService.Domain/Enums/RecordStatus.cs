namespace Hdos.DataMatchingService.Domain.Enums;

public enum RecordStatus
{
    Pending    = 0,
    Processing = 1,
    Matched    = 2,
    Duplicate  = 3,
    Failed     = 4
}
