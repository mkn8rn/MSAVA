using System.Text.Json;
using System.Text.Json.Serialization;
using MSAVA_Shared.Models;

namespace MSAVA_App.Services.Api;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(Guid))]
[JsonSerializable(typeof(LoginRequestDTO))]
[JsonSerializable(typeof(LoginResponseDTO))]
[JsonSerializable(typeof(SessionDTO))]
[JsonSerializable(typeof(List<SearchFileDataDTO>))]
internal sealed partial class AppJsonSerializerContext : JsonSerializerContext;
