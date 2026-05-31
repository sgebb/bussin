using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Azure.Cosmos;
using Bussin.Backend.Models;

namespace Bussin.Backend.Serialization;

[JsonSerializable(typeof(TrackLoginRequest))]
[JsonSerializable(typeof(LoginRecord))]
[JsonSerializable(typeof(HealthStatus))]
[JsonSerializable(typeof(TenantAccess))]
[JsonSerializable(typeof(TrackLoginResponse))]
internal partial class BussinJsonContext : JsonSerializerContext
{
}

public class CosmosSystemTextJsonSerializer : CosmosSerializer
{
    private readonly JsonSerializerContext _context;

    public CosmosSystemTextJsonSerializer(JsonSerializerContext context)
    {
        _context = context;
    }

    public override T FromStream<T>(Stream stream)
    {
        using (stream)
        {
            if (stream.Length == 0) return default!;
            return (T)JsonSerializer.Deserialize(stream, typeof(T), _context)!;
        }
    }

    public override Stream ToStream<T>(T input)
    {
        var memoryStream = new MemoryStream();
        JsonSerializer.Serialize(memoryStream, input, typeof(T), _context);
        memoryStream.Position = 0;
        return memoryStream;
    }
}
