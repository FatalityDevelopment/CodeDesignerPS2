using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace CodeDesigner.Ps2.Bridge.Services
{
    internal sealed class SymbolDatabaseService : ICommandService
    {
        private readonly IBridgeContext _ctx;
        public SymbolDatabaseService(IBridgeContext ctx) => _ctx = ctx;

        public void Register(CommandRegistry reg)
        {
            reg.Add(c => HandleListFunctions(c.Offset, c.MaxCount),                                     "LISTFUNCTIONS", "LIST_FUNCTIONS", "PINEFUNCTIONS");
            reg.Add(c => HandleGetSymbolByName(c.SymbolName),                                           "GETSYMBOLBYNAME", "GET_SYMBOL_BY_NAME");
            reg.Add(c => HandleListGlobals(c.Offset, c.MaxCount),                                       "LISTGLOBALS", "LIST_GLOBALS", "PINEGLOBALS");
            reg.Add(c => HandleGetLocals(c.Address),                                                    "GETLOCALS", "GET_LOCALS");
        }

        private string HandleListFunctions(uint? offset, ushort? maxCount)
        {
            if (_ctx.PineClient != null)
            {
                try
                {
                    var (total, symbols) = _ctx.PineClient!.ListFunctions(offset ?? 0, maxCount ?? 256);
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
                catch (Exception ex) { return Responses.Error($"PINE ListFunctions failed: {ex.Message}"); }
            }
            return Responses.Error("LISTFUNCTIONS requires PINE mode");
        }

        private string HandleGetSymbolByName(string? name)
        {
            if (string.IsNullOrEmpty(name)) return Responses.Error("SymbolName is required for GETSYMBOLBYNAME");
            if (_ctx.PineClient != null)
            {
                try
                {
                    var sym = _ctx.PineClient!.GetSymbolByName(name);
                    if (sym == null) return Responses.Error($"Symbol '{name}' not found");
                    return JsonSerializer.Serialize(new CommandResponse
                    {
                        Success       = true,
                        Command       = "GETSYMBOLBYNAME",
                        SymbolAddress = $"0x{sym.Value.Address:X8}",
                        SymbolName    = name,
                        SymbolSize    = sym.Value.Size,
                    });
                }
                catch (Exception ex) { return Responses.Error($"PINE GetSymbolByName failed: {ex.Message}"); }
            }
            return Responses.Error("GETSYMBOLBYNAME requires PINE mode");
        }

        private string HandleListGlobals(uint? offset, ushort? maxCount)
        {
            if (_ctx.PineClient != null)
            {
                try
                {
                    var (total, symbols) = _ctx.PineClient!.ListGlobals(offset ?? 0, maxCount ?? 256);
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
                catch (Exception ex) { return Responses.Error($"PINE ListGlobals failed: {ex.Message}"); }
            }
            return Responses.Error("LISTGLOBALS requires PINE mode");
        }

        private string HandleGetLocals(uint? address)
        {
            if (address == null) return Responses.Error("Address is required for GETLOCALS");
            if (_ctx.PineClient != null)
            {
                try
                {
                    var locals = _ctx.PineClient!.GetLocals(address.Value);
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
                catch (Exception ex) { return Responses.Error($"PINE GetLocals failed: {ex.Message}"); }
            }
            return Responses.Error("GETLOCALS requires PINE mode");
        }
    }
}
