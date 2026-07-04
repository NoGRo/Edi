using System;
using System.Globalization;
using Newtonsoft.Json;

namespace Edi.Core.Device.Handy.Transport.BLE
{
    /// <summary>
    /// Maps high-level Handy commands to RPC payloads.
    /// Decouples the device logic from the RPC protocol details.
    /// </summary>
    public interface IHandyRpcMapper
    {
        /// <summary>
        /// Converts a REST endpoint and payload to an RPC command type and JSON payload.
        /// </summary>
        (RpcCommandType, string?) MapToRpc(string endpoint, string? restPayload);

        /// <summary>
        /// Extracts relevant information from an RPC response.
        /// </summary>
        T? ParseResponse<T>(RpcResponse response) where T : class;
    }

    /// <summary>
    /// Standard implementation of RPC mapper for Handy device.
    /// </summary>
    public sealed class HandyRpcMapper : IHandyRpcMapper
    {
        public (RpcCommandType, string?) MapToRpc(string endpoint, string? restPayload)
        {
            return endpoint switch
            {
                // Mode control
                "v2/mode" => (RpcCommandType.RequestModeSet, restPayload),
                "v2/modes" => (RpcCommandType.RequestModeGet, null),
                
                // HSP offsets and modes
                "v2/hstp/offset" => (RpcCommandType.RequestHspSetup, restPayload),
                "v2/hstp/setup" => (RpcCommandType.RequestHspSetup, restPayload),
                
                // HSP v3 protocol
                "v3/hsp/setup" => (RpcCommandType.RequestHspSetup, restPayload),
                "v3/hsp/add" => (RpcCommandType.RequestHspAdd, restPayload),
                "v3/hsp/play" => (RpcCommandType.RequestHspPlay, restPayload),
                "v3/hsp/stop" => (RpcCommandType.RequestHspStop, restPayload),
                
                // HAMP mode
                "v2/hamp/start" => (RpcCommandType.RequestHampStart, restPayload),
                "v2/hamp/velocity" => (RpcCommandType.RequestHampVelocitySet, restPayload),
                "v2/slide" => (RpcCommandType.RequestHampVelocitySet, restPayload),
                
                // Device info and capabilities
                "v2/info" => (RpcCommandType.RequestCapabilitiesGet, null),
                "v3/capabilities" => (RpcCommandType.RequestCapabilitiesGet, null),
                
                // Stop/disconnect
                "v2/stop" => (RpcCommandType.RequestStopCurrentMode, null),
                
                _ => (RpcCommandType.Unknown, restPayload)
            };
        }

        public T? ParseResponse<T>(RpcResponse response) where T : class
        {
            try
            {
                if (response?.Result == null)
                    return null;

                return JsonConvert.DeserializeObject<T>(response.Result);
            }
            catch (JsonException ex)
            {
                throw new BleHandyException($"Failed to parse RPC response to {typeof(T).Name}: {ex.Message}", ex);
            }
        }
    }

    /// <summary>
    /// RPC command builders for common Handy operations.
    /// </summary>
    public static class RpcCommandBuilder
    {
        /// <summary>
        /// Builds a capabilities request.
        /// </summary>
        public static RpcRequest BuildCapabilitiesRequest(uint requestId)
        {
            return new RpcRequest
            {
                Id = requestId,
                Method = RpcCommandType.RequestCapabilitiesGet.ToString(),
                Payload = null
            };
        }

        /// <summary>
        /// Builds a mode get request.
        /// </summary>
        public static RpcRequest BuildModeGetRequest(uint requestId)
        {
            return new RpcRequest
            {
                Id = requestId,
                Method = RpcCommandType.RequestModeGet.ToString(),
                Payload = null
            };
        }

        /// <summary>
        /// Builds a mode set request.
        /// </summary>
        public static RpcRequest BuildModeSetRequest(uint requestId, int mode)
        {
            return new RpcRequest
            {
                Id = requestId,
                Method = RpcCommandType.RequestModeSet.ToString(),
                Payload = JsonConvert.SerializeObject(new { mode })
            };
        }

        /// <summary>
        /// Builds an HSP setup request.
        /// </summary>
        public static RpcRequest BuildHspSetupRequest(uint requestId, int streamId, int offset = 0)
        {
            return new RpcRequest
            {
                Id = requestId,
                Method = RpcCommandType.RequestHspSetup.ToString(),
                Payload = JsonConvert.SerializeObject(new 
                { 
                    stream_id = streamId,
                    offset = offset
                })
            };
        }

        /// <summary>
        /// Builds an HSP add points request.
        /// </summary>
        public static RpcRequest BuildHspAddRequest(uint requestId, object pointsPayload)
        {
            return new RpcRequest
            {
                Id = requestId,
                Method = RpcCommandType.RequestHspAdd.ToString(),
                Payload = JsonConvert.SerializeObject(pointsPayload)
            };
        }

        /// <summary>
        /// Builds an HSP play request.
        /// </summary>
        public static RpcRequest BuildHspPlayRequest(uint requestId, object playPayload)
        {
            return new RpcRequest
            {
                Id = requestId,
                Method = RpcCommandType.RequestHspPlay.ToString(),
                Payload = JsonConvert.SerializeObject(playPayload)
            };
        }

        /// <summary>
        /// Builds an HSP stop request.
        /// </summary>
        public static RpcRequest BuildHspStopRequest(uint requestId)
        {
            return new RpcRequest
            {
                Id = requestId,
                Method = RpcCommandType.RequestHspStop.ToString(),
                Payload = null
            };
        }

        /// <summary>
        /// Builds a velocity set request (for HAMP mode).
        /// </summary>
        public static RpcRequest BuildVelocitySetRequest(uint requestId, float velocity)
        {
            return new RpcRequest
            {
                Id = requestId,
                Method = RpcCommandType.RequestHampVelocitySet.ToString(),
                Payload = JsonConvert.SerializeObject(new { velocity = velocity.ToString("F2", CultureInfo.InvariantCulture) })
            };
        }

        /// <summary>
        /// Builds a stop current mode request.
        /// </summary>
        public static RpcRequest BuildStopRequest(uint requestId)
        {
            return new RpcRequest
            {
                Id = requestId,
                Method = RpcCommandType.RequestStopCurrentMode.ToString(),
                Payload = null
            };
        }
    }
}
