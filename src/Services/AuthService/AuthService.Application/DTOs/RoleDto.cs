namespace Hdos.AuthService.Application.DTOs;

public sealed record RoleDto(Guid Id, string Name, string Description, List<PermissionDto> Permissions);
public sealed record PermissionDto(Guid Id, string Resource, string Action, string Description, string Key);
public sealed record UserRoleDto(Guid UserId, Guid RoleId, string RoleName);
