using System.Text.Json.Serialization;
using static Mizon.Server.API.Services.JwtTokenService;

namespace Mizon.Server.API.JsonSerializerContexts;

[JsonSerializable(typeof(JwtHeader))]
[JsonSerializable(typeof(JwtPayload))]
[JsonSerializable(typeof(BaseApiError))]


internal partial class JwtJsonContext : JsonSerializerContext
{
}
