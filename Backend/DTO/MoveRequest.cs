namespace Backend.DTO;

public record MoveRequest(string sourcePath, string targetPath, bool canOverride);
