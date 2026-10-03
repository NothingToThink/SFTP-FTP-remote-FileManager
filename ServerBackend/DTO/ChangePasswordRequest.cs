namespace ProfileServer.DTO;

public record ChangePasswordRequest(string OldPassword, string NewPassword);
