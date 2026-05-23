using System.Text.Json.Serialization;

namespace Core.Models.Credentials;


[JsonDerivedType(typeof(PasswordAuth), "password")]
[JsonDerivedType(typeof(KeyAuth), "key")]
[JsonDerivedType(typeof(AnonymousAuth), "anonymous")]
public abstract record AuthData;
