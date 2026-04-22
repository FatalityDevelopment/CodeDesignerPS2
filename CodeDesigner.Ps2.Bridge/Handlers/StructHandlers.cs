using System;
using System.Linq;
using System.Text.Json;

namespace CodeDesigner.Ps2.Bridge
{
    partial class SocketServer
    {
        /// <summary>
        /// Handle GETSTRUCTS — returns all struct definitions sorted by name.
        /// </summary>
        private string HandleGetStructs()
        {
            var structs = _structs.Values
                .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return JsonSerializer.Serialize(new CommandResponse
            {
                Success = true,
                Command = "GETSTRUCTS",
                Structs = structs
            });
        }

        /// <summary>
        /// Handle ADDSTRUCT — creates or updates a struct definition, then persists.
        /// </summary>
        private string HandleAddStruct(string? structName, string? baseAddress)
        {
            if (string.IsNullOrWhiteSpace(structName))
                return CreateErrorResponse("StructName is required for ADDSTRUCT");
            if (string.IsNullOrWhiteSpace(baseAddress))
                return CreateErrorResponse("StructBaseAddress is required for ADDSTRUCT");

            var name = structName.Trim();
            var addr = baseAddress.Trim();

            if (_structs.TryGetValue(name, out var existing))
            {
                existing.BaseAddress = addr;
            }
            else
            {
                _structs[name] = new StructEntry { Name = name, BaseAddress = addr };
            }

            SaveStructs();
            return CreateSuccessResponse($"Struct '{name}' saved with base address {addr}");
        }

        /// <summary>
        /// Handle DELETESTRUCT — removes a struct definition by name, then persists.
        /// </summary>
        private string HandleDeleteStruct(string? structName)
        {
            if (string.IsNullOrWhiteSpace(structName))
                return CreateErrorResponse("StructName is required for DELETESTRUCT");

            var name = structName.Trim();
            if (!_structs.Remove(name))
                return CreateErrorResponse($"No struct found with name '{name}'");

            SaveStructs();
            return CreateSuccessResponse($"Struct '{name}' removed");
        }

        /// <summary>
        /// Handle ADDSTRUCTMEMBER — adds or updates a member field in a struct, then persists.
        /// </summary>
        private string HandleAddStructMember(string? structName, int? memberOffset, string? memberName)
        {
            if (string.IsNullOrWhiteSpace(structName))
                return CreateErrorResponse("StructName is required for ADDSTRUCTMEMBER");
            if (memberOffset == null)
                return CreateErrorResponse("MemberOffset is required for ADDSTRUCTMEMBER");
            if (string.IsNullOrWhiteSpace(memberName))
                return CreateErrorResponse("MemberName is required for ADDSTRUCTMEMBER");

            var name = structName.Trim();
            if (!_structs.TryGetValue(name, out var entry))
                return CreateErrorResponse($"No struct found with name '{name}'");

            var trimmedMember = memberName.Trim();
            var existing = entry.Members.FirstOrDefault(m => m.Offset == memberOffset.Value);
            if (existing != null)
            {
                existing.Name = trimmedMember;
            }
            else
            {
                entry.Members.Add(new StructMemberEntry { Offset = memberOffset.Value, Name = trimmedMember });
                entry.Members.Sort((a, b) => a.Offset.CompareTo(b.Offset));
            }

            SaveStructs();
            return CreateSuccessResponse($"Member '{trimmedMember}' at offset 0x{memberOffset.Value:X} added to struct '{name}'");
        }

        /// <summary>
        /// Persist struct definitions to ps2MemMap.json alongside the label symbols.
        /// Delegates to <see cref="SocketServer.SaveMemoryMap"/> to avoid duplication.
        /// </summary>
        private void SaveStructs()
        {

        }
    }
}
