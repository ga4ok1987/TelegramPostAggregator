using TelegramPostAggregator.Application.DTOs;

namespace TelegramPostAggregator.Application.Abstractions.Services;

public interface IAdminUserService
{
    Task<IReadOnlyList<AdminUserDto>> ListAsync(Guid currentAdminUserId, CancellationToken cancellationToken = default);

    Task<AdminUserDetailDto?> GetAsync(Guid adminUserId, Guid currentAdminUserId, CancellationToken cancellationToken = default);

    Task<AdminUserCommandResultDto> CreateAsync(AdminUserCreateDto request, Guid currentAdminUserId, CancellationToken cancellationToken = default);

    Task<AdminUserCommandResultDto> UpdateAsync(Guid adminUserId, AdminUserUpdateDto request, Guid currentAdminUserId, CancellationToken cancellationToken = default);

    Task<AdminCommandResultDto> SetPasswordAsync(Guid adminUserId, string newPassword, Guid currentAdminUserId, CancellationToken cancellationToken = default);

    Task<AdminCommandResultDto> DeleteAsync(Guid adminUserId, Guid currentAdminUserId, CancellationToken cancellationToken = default);
}
