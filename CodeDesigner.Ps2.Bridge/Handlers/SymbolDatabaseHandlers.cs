using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace CodeDesigner.Ps2.Bridge
{
    partial class SocketServer
    {
        private string HandleListFunctions(uint? offset, ushort? maxCount)
        {
            if (IsPineMode)
            {
                try
                {
                    var (total, symbols) = PineClient!.ListFunctions(offset ?? 0, maxCount ?? 256);
                    return JsonSerializer.Serialize(new CommandResponse
                    {
                        Success       = true,
                        Command       = "LISTFUNCTIONS",
                        TotalSymbols  = total,
                        PineSymbols   = symbols.Select(s => new PineSymbolInfo
                        {
                            Address = $"0x{s.Address:X8}",
                            Size    = s.Size,
                            Name    = s.Name,
                        }).ToList(),
                    });
                }
                catch (Exception ex) { return CreateErrorResponse($"PINE ListFunctions failed: {ex.Message}"); }
            }
            return CreateErrorResponse("LISTFUNCTIONS requires PINE mode");
        }

        private string HandleGetSymbolByName(string? name)
        {
            if (string.IsNullOrEmpty(name)) return CreateErrorResponse("SymbolName is required for GETSYMBOLBYNAME");
            if (IsPineMode)
            {
                try
                {
                    var sym = PineClient!.GetSymbolByName(name);
                    if (sym == null) return CreateErrorResponse($"Symbol '{name}' not found");
                    return JsonSerializer.Serialize(new CommandResponse
                    {
                        Success       = true,
                        Command       = "GETSYMBOLBYNAME",
                        SymbolAddress = $"0x{sym.Value.Address:X8}",
                        SymbolName    = name,
                        SymbolSize    = sym.Value.Size,
                    });
                }
                catch (Exception ex) { return CreateErrorResponse($"PINE GetSymbolByName failed: {ex.Message}"); }
            }
            return CreateErrorResponse("GETSYMBOLBYNAME requires PINE mode");
        }

        private string HandleListGlobals(uint? offset, ushort? maxCount)
        {
            if (IsPineMode)
            {
                try
                {
                    var (total, symbols) = PineClient!.ListGlobals(offset ?? 0, maxCount ?? 256);
                    return JsonSerializer.Serialize(new CommandResponse
                    {
                        Success       = true,
                        Command       = "LISTGLOBALS",
                        TotalSymbols  = total,
                        PineSymbols   = symbols.Select(s => new PineSymbolInfo
                        {
                            Address = $"0x{s.Address:X8}",
                            Size    = s.Size,
                            Name    = s.Name,
                        }).ToList(),
                    });
                }
                catch (Exception ex) { return CreateErrorResponse($"PINE ListGlobals failed: {ex.Message}"); }
            }
            return CreateErrorResponse("LISTGLOBALS requires PINE mode");
        }

        private string HandleGetLocals(uint? address)
        {
            if (address == null) return CreateErrorResponse("Address is required for GETLOCALS");
            if (IsPineMode)
            {
                try
                {
                    var locals = PineClient!.GetLocals(address.Value);
                    return JsonSerializer.Serialize(new CommandResponse
                    {
                        Success    = true,
                        Command    = "GETLOCALS",
                        LocalVars  = locals.Select(l => new PineLocalInfo
                        {
                            StorageType = l.StorageType switch { 0 => "global", 1 => "register", 2 => "stack", _ => $"0x{l.StorageType:X2}" },
                            Value       = l.Value,
                            Name        = l.Name,
                        }).ToList(),
                    });
                }
                catch (Exception ex) { return CreateErrorResponse($"PINE GetLocals failed: {ex.Message}"); }
            }
            return CreateErrorResponse("GETLOCALS requires PINE mode");
        }
    }
}
