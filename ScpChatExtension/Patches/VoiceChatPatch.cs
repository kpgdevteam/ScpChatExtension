﻿using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Mirror;
using NorthwoodLib.Pools;
using PlayerRoles;
using UnityEngine;
using VoiceChat.Networking;

namespace ScpChatExtension.Patches;

[HarmonyPatch(typeof(VoiceTransceiver), nameof(VoiceTransceiver.ServerReceiveMessage))]
public class VoiceChatPatch
{
    private static MethodInfo GetSendMethod()
    {
        foreach (MethodInfo method in typeof(NetworkConnection).GetMethods())
        {
            if (method.Name is nameof(NetworkConnection.Send) && method.GetGenericArguments().Length != 0)
                return method.MakeGenericMethod(typeof(VoiceMessage));
        }

        return null;
    }
    
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        List<CodeInstruction> newInstructions = ListPool<CodeInstruction>.Shared.Rent(instructions);

        Label skip = generator.DefineLabel();
        Label spectatorSkip = generator.DefineLabel();

        int index = newInstructions.FindLastIndex(x => x.opcode == OpCodes.Brfalse_S) - 1;
        
        newInstructions[index].labels.Add(skip);

        newInstructions.InsertRange(index, new List<CodeInstruction>()
        {
            // if (voiceChatChannel is not VoiceChatChannel.ScpChat) skip;
            new (OpCodes.Ldloc_2),
            new (OpCodes.Ldc_I4_3),
            new (OpCodes.Ceq),
            new (OpCodes.Brfalse_S, skip),

            // if (referenceHub.CurrentRole.Team == Team.SCPs) skip;
            new (OpCodes.Ldloc_S, 4),
            new (OpCodes.Ldfld, AccessTools.Field(typeof(ReferenceHub), nameof(ReferenceHub.roleManager))),
            new (OpCodes.Callvirt, AccessTools.PropertyGetter(typeof(PlayerRoleManager), nameof(PlayerRoleManager.CurrentRole))),
            new (OpCodes.Callvirt, AccessTools.PropertyGetter(typeof(PlayerRoleBase), nameof(PlayerRoleBase.Team))),
            new (OpCodes.Brfalse_S, skip),
            
            // if (referenceHub.roleManager.CurrentRole.RoleTypeId == RoleTypeId.Spectator) spectatorSkip;
            new (OpCodes.Ldloc_S, 4),
            new (OpCodes.Ldfld, AccessTools.Field(typeof(ReferenceHub), nameof(ReferenceHub.roleManager))),
            new (OpCodes.Callvirt, AccessTools.PropertyGetter(typeof(PlayerRoleManager), nameof(PlayerRoleManager.CurrentRole))),
            new (OpCodes.Callvirt, AccessTools.PropertyGetter(typeof(PlayerRoleBase), nameof(PlayerRoleBase.RoleTypeId))),
            new (OpCodes.Ldc_I4_2),
            new (OpCodes.Ceq),
            new (OpCodes.Brtrue_S, spectatorSkip),
            
            // if (!AllowedRoles.Contains(msg.Speaker.roleManager.CurrentRole.RoleTypeId)) skip;
            new (OpCodes.Ldarg_1),
            new (OpCodes.Ldfld, AccessTools.Field(typeof(VoiceMessage), nameof(VoiceMessage.Speaker))),
            new (OpCodes.Ldfld, AccessTools.Field(typeof(ReferenceHub), nameof(ReferenceHub.roleManager))),
            new (OpCodes.Callvirt, AccessTools.PropertyGetter(typeof(PlayerRoleManager), nameof(PlayerRoleManager.CurrentRole))),
            new (OpCodes.Callvirt, AccessTools.PropertyGetter(typeof(PlayerRoleBase), nameof(PlayerRoleBase.RoleTypeId))),
            new (OpCodes.Ldfld, AccessTools.Field(typeof(EntryPoint), nameof(EntryPoint.Config))),
            new (OpCodes.Callvirt, AccessTools.PropertyGetter(typeof(PluginConfig), nameof(PluginConfig.AllowedRoles))),
            new (OpCodes.Callvirt, AccessTools.Method(typeof(HashSet<RoleTypeId>), nameof(HashSet<RoleTypeId>.Contains))),
            new (OpCodes.Brfalse_S, skip),

            // if(Vector3.Distance(msg.Speaker.transform.position, referenceHub.transform.position) >= MaxProximityDistance) skip;
            new (OpCodes.Ldarg_1),
            new (OpCodes.Ldfld, AccessTools.Field(typeof(VoiceMessage), nameof(VoiceMessage.Speaker))),
            new (OpCodes.Callvirt, AccessTools.PropertyGetter(typeof(ReferenceHub), nameof(ReferenceHub.transform))),
            new (OpCodes.Callvirt, AccessTools.PropertyGetter(typeof(Transform), nameof(Transform.position))),
            new (OpCodes.Ldloc_S, 4),
            new (OpCodes.Callvirt, AccessTools.PropertyGetter(typeof(ReferenceHub), nameof(ReferenceHub.transform))),
            new (OpCodes.Callvirt, AccessTools.PropertyGetter(typeof(Transform), nameof(Transform.position))),
            new (OpCodes.Call, AccessTools.Method(typeof(Vector3), nameof(Vector3.Distance))),
            new (OpCodes.Ldc_R4, EntryPoint.Config.MaxProximityDistance),
            new (OpCodes.Bge_S, skip),

            // msg.Channel = VoiceChatChannel.Proximity;
            // referenceHub.connectionToClient.Send<VoiceMessage>(msg, 0);
            new CodeInstruction(OpCodes.Ldarga_S, 1).WithLabels(spectatorSkip),
            new (OpCodes.Ldc_I4_1),
            new (OpCodes.Stfld, AccessTools.Field(typeof(VoiceMessage), nameof(VoiceMessage.Channel))),
            new (OpCodes.Ldloc_S, 4),
            new (OpCodes.Callvirt, AccessTools.PropertyGetter(typeof(ReferenceHub), nameof(ReferenceHub.connectionToClient))),
            new (OpCodes.Ldarg_1),
            new (OpCodes.Ldc_I4_0),
            new (OpCodes.Callvirt, GetSendMethod())
        });

        foreach (CodeInstruction instruction in newInstructions)
            yield return instruction;
        
        ListPool<CodeInstruction>.Shared.Return(newInstructions);
    }
}