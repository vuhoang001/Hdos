namespace Hdos.DynamicFormService.Domain.Enums;

// Kind quyết định cách FE parse response của Operation.
// Single → response.data là object đơn lẻ (vd: GET /dm/records/{id})
// List   → response.data là array (vd: GET /dm/records?...)
public enum OperationKind
{
    Single = 0,
    List   = 1
}
