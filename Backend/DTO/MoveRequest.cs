namespace Backend.DTO;

public record MoveRequest(List<string> sourcePaths, string targetPath, bool overwrite);
