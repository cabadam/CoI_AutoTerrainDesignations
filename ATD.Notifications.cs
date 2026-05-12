// Auto Terrain Designations
// Copyright (c) 2026 Kayser
// Licensed under the MIT License.
//
// Unofficial mod for Captain of Industry. Captain of Industry, MaFi Games, and
// related trademarks, code, and assets belong to MaFi Games. This repository is
// intended to contain only original mod code/configuration; if MaFi Games material
// is included by mistake, I intend to correct it promptly upon discovery or notice.
using System.Collections.Generic;
using System.Linq;
using Mafi;
using Mafi.Core;
using Mafi.Core.Buildings.Towers;
using Mafi.Core.Entities;
using Mafi.Core.Mods;
using Mafi.Core.Notifications;
using Mafi.Core.Prototypes;

namespace AutoTerrainDesignations
{
    internal static class AtdNotifications
    {
        internal static readonly EntityNotificationProto.ID RampAccessFailedId =
            new EntityNotificationProto.ID("ATD_RampAccessFailed");
        internal static readonly EntityNotificationProto.ID RampAccessTruncatedId =
            new EntityNotificationProto.ID("ATD_RampAccessTruncated");
        internal static readonly EntityNotificationProto.ID RampAccessNotAccessibleId =
            new EntityNotificationProto.ID("ATD_RampAccessNotAccessible");
        internal static readonly EntityNotificationProto.ID FarmingCompleteId =
            new EntityNotificationProto.ID("ATD_FarmingComplete");
        internal static readonly EntityNotificationProto.ID ExcavatorCompletedId =
            new EntityNotificationProto.ID("ATD_ExcavatorCompleted");

        private static readonly HashSet<string> s_protoIds = new HashSet<string>
        {
            "ATD_RampAccessWarning",
            RampAccessFailedId.Value,
            RampAccessTruncatedId.Value,
            RampAccessNotAccessibleId.Value,
            FarmingCompleteId.Value,
            ExcavatorCompletedId.Value,
        };

        internal static void RegisterPrototypes(ProtoRegistrator registrator)
        {
            RegisterWarning(
                registrator,
                AtdLocalization.Tr(
                    "notification.ramp_access_failed",
                    "[ATD] {entity} could not start an access ramp"),
                RampAccessFailedId);
            RegisterWarning(
                registrator,
                AtdLocalization.Tr(
                    "notification.ramp_access_truncated",
                    "[ATD] {entity} could not fit a full access ramp"),
                RampAccessTruncatedId);
            RegisterWarning(
                registrator,
                AtdLocalization.Tr(
                    "notification.ramp_access_not_accessible",
                    "[ATD] {entity} could not path to the ramp"),
                RampAccessNotAccessibleId);
            RegisterSuccess(
                registrator,
                AtdLocalization.Tr(
                    "notification.farming_complete",
                    "[ATD] {entity} farming preparation and filling complete"),
                FarmingCompleteId);
            RegisterSuccess(
                registrator,
                AtdLocalization.Tr(
                    "notification.excavator_completed",
                    "[ATD] {entity} completed an excavator"),
                ExcavatorCompletedId,
                "Assets/Unity/UserInterface/Toolbar/Mining.svg");
        }

        private static void RegisterWarning(ProtoRegistrator registrator, string message, EntityNotificationProto.ID id)
        {
            registrator.NotificationProtoBuilder
                .Start(message, id)
                .SetType(NotificationType.Continuous)
                .SetStyle(NotificationStyle.Warning)
                .MuteAudio()
                .AddIcon("Assets/Unity/UserInterface/General/Warning128.png")
                .AddEntityIcon("Assets/Unity/UserInterface/EntityIcons/Warning.png")
                .BuildAndAdd();
        }

        private static void RegisterSuccess(
            ProtoRegistrator registrator,
            string message,
            EntityNotificationProto.ID id,
            string iconPath = "Assets/Unity/UserInterface/EntityIcons/Designation.png")
        {
            registrator.NotificationProtoBuilder
                .Start(message, id)
                .SetType(NotificationType.OneTimeOnly)
                .SetStyle(NotificationStyle.Success)
                .SetTimeToLive(Duration.FromSec(20))
                .MuteAudio()
                .AddIcon(iconPath)
                .BuildAndAdd(doNotRequireEntityIcon: true);
        }

        internal static bool IsAtdProto(NotificationProto proto)
        {
            return s_protoIds.Contains(proto.Id.Value);
        }
    }

    public static partial class AutoDepthDesignation
    {
        private enum TransientNotificationKind
        {
            RampAccessFailed,
            RampAccessTruncated,
            RampAccessNotAccessible,
        }

        private readonly struct TransientNotificationKey
        {
            public readonly EntityId EntityId;
            public readonly TransientNotificationKind Kind;

            public TransientNotificationKey(EntityId entityId, TransientNotificationKind kind)
            {
                EntityId = entityId;
                Kind = kind;
            }

            public override bool Equals(object? obj)
            {
                return obj is TransientNotificationKey other
                    && EntityId.Equals(other.EntityId)
                    && Kind == other.Kind;
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (EntityId.GetHashCode() * 397) ^ (int)Kind;
                }
            }
        }

        private static INotificationsManager? s_notificationsManager;
        private static EntityNotificationProto? s_rampAccessFailedNotificationProto;
        private static EntityNotificationProto? s_rampAccessTruncatedNotificationProto;
        private static EntityNotificationProto? s_rampAccessNotAccessibleNotificationProto;
        private static EntityNotificationProto? s_farmingCompleteNotificationProto;
        private static EntityNotificationProto? s_excavatorCompletedNotificationProto;
        private static readonly Dictionary<TransientNotificationKey, NotificationId> s_transientNotificationsByKey =
            new Dictionary<TransientNotificationKey, NotificationId>();

        private static void InitializeTransientNotifications(INotificationsManager? notificationsManager, ProtosDb protosDb)
        {
            s_notificationsManager = notificationsManager;

            TryInitializeTransientNotificationProto(protosDb, AtdNotifications.RampAccessFailedId, ref s_rampAccessFailedNotificationProto);
            TryInitializeTransientNotificationProto(protosDb, AtdNotifications.RampAccessTruncatedId, ref s_rampAccessTruncatedNotificationProto);
            TryInitializeTransientNotificationProto(protosDb, AtdNotifications.RampAccessNotAccessibleId, ref s_rampAccessNotAccessibleNotificationProto);
            TryInitializeTransientNotificationProto(protosDb, AtdNotifications.FarmingCompleteId, ref s_farmingCompleteNotificationProto);
            TryInitializeTransientNotificationProto(protosDb, AtdNotifications.ExcavatorCompletedId, ref s_excavatorCompletedNotificationProto);
        }

        private static void TryInitializeTransientNotificationProto(
            ProtosDb protosDb,
            EntityNotificationProto.ID id,
            ref EntityNotificationProto? proto)
        {
            if (protosDb.TryGetProto(id, out EntityNotificationProto resolvedProto))
                proto = resolvedProto;
            else
                Log.Warning("[ATD] Transient notification proto not found: " + id.Value);
        }

        private static void ResetTransientNotifications()
        {
            s_notificationsManager = null;
            s_rampAccessFailedNotificationProto = null;
            s_rampAccessTruncatedNotificationProto = null;
            s_rampAccessNotAccessibleNotificationProto = null;
            s_farmingCompleteNotificationProto = null;
            s_excavatorCompletedNotificationProto = null;
            s_transientNotificationsByKey.Clear();
        }

        private static void UpdateTowerRampWarningNotification(IAreaManagingTower tower, RampPlacementOutcome outcome)
        {
            if (!TryGetRampWarningNotification(outcome, out TransientNotificationKind kind, out EntityNotificationProto? proto))
            {
                ClearTowerRampWarningNotification(tower);
                return;
            }

            if (HasTransientTowerNotification(tower, kind))
                return;

            ClearTowerRampWarningNotification(tower);
            AddTransientTowerNotification(tower, kind, proto);
        }

        private static bool TryGetRampWarningNotification(
            RampPlacementOutcome outcome,
            out TransientNotificationKind kind,
            out EntityNotificationProto? proto)
        {
            if (outcome == RampPlacementOutcome.Failed)
            {
                kind = TransientNotificationKind.RampAccessFailed;
                proto = s_rampAccessFailedNotificationProto;
                return true;
            }

            if (outcome == RampPlacementOutcome.Truncated)
            {
                kind = TransientNotificationKind.RampAccessTruncated;
                proto = s_rampAccessTruncatedNotificationProto;
                return true;
            }

            if (outcome == RampPlacementOutcome.NotAccessible)
            {
                kind = TransientNotificationKind.RampAccessNotAccessible;
                proto = s_rampAccessNotAccessibleNotificationProto;
                return true;
            }

            kind = default;
            proto = null;
            return false;
        }

        internal static void PurgeTransientNotificationsForSave()
        {
            if (s_notificationsManager == null)
                return;

            foreach (NotificationId notificationId in s_transientNotificationsByKey.Values.ToList())
                s_notificationsManager.RemoveNotification(notificationId);
            s_transientNotificationsByKey.Clear();

            foreach (INotification notification in s_notificationsManager.FetchAllNotifications().ToList())
            {
                if (AtdNotifications.IsAtdProto(notification.Proto))
                    s_notificationsManager.RemoveNotification(notification.NotificationId);
            }
        }

        internal static void RestoreTransientNotificationsAfterSave()
        {
            if (s_entitiesManager == null)
                return;

            foreach (KeyValuePair<EntityId, ATDTowerSettings> kvp in s_towerSettingsByEntityId.ToList())
            {
                if (!kvp.Value.LastRampOutcome.HasValue)
                    continue;

                if (s_entitiesManager.TryGetEntity<IEntity>(kvp.Key, out IEntity entity) && entity is IAreaManagingTower tower)
                    UpdateTowerRampWarningNotification(tower, kvp.Value.LastRampOutcome.Value);
            }
        }

        private static void ClearTowerRampWarningNotification(IAreaManagingTower tower)
        {
            ClearTransientTowerNotification(tower, TransientNotificationKind.RampAccessFailed);
            ClearTransientTowerNotification(tower, TransientNotificationKind.RampAccessTruncated);
            ClearTransientTowerNotification(tower, TransientNotificationKind.RampAccessNotAccessible);
        }

        private static void AddFarmingCompleteNotification(IAreaManagingTower tower)
        {
            if (s_notificationsManager == null || s_farmingCompleteNotificationProto == null)
                return;

            if (!(tower is IObjectWithTitle objectWithTitle))
                return;

            s_notificationsManager.AddNotification(
                s_farmingCompleteNotificationProto,
                Option<IObjectWithTitle>.Create(objectWithTitle),
                Option.None);
        }

        private static void AddExcavatorCompletedNotification(IObjectWithTitle objectWithTitle)
        {
            if (s_notificationsManager == null || s_excavatorCompletedNotificationProto == null)
                return;

            s_notificationsManager.AddNotification(
                s_excavatorCompletedNotificationProto,
                Option<IObjectWithTitle>.Create(objectWithTitle),
                Option.None);
        }

        private static bool HasTransientTowerNotification(IAreaManagingTower tower, TransientNotificationKind kind)
        {
            if (!TryGetTowerEntityId(tower, out EntityId entityId))
                return false;

            TransientNotificationKey key = new TransientNotificationKey(entityId, kind);
            return s_transientNotificationsByKey.ContainsKey(key);
        }

        private static void AddTransientTowerNotification(
            IAreaManagingTower tower,
            TransientNotificationKind kind,
            EntityNotificationProto? proto)
        {
            if (s_notificationsManager == null || proto == null)
                return;

            if (!TryGetTowerEntityId(tower, out EntityId entityId))
                return;

            TransientNotificationKey key = new TransientNotificationKey(entityId, kind);
            if (s_transientNotificationsByKey.ContainsKey(key))
                return;

            if (!(tower is IObjectWithTitle objectWithTitle))
                return;

            NotificationId notificationId = s_notificationsManager.AddNotification(
                proto,
                Option<IObjectWithTitle>.Create(objectWithTitle),
                Option.None);
            s_transientNotificationsByKey[key] = notificationId;
        }

        private static void ClearTransientTowerNotification(IAreaManagingTower tower, TransientNotificationKind kind)
        {
            if (s_notificationsManager == null)
                return;

            if (!TryGetTowerEntityId(tower, out EntityId entityId))
                return;

            TransientNotificationKey key = new TransientNotificationKey(entityId, kind);
            if (s_transientNotificationsByKey.TryGetValue(key, out NotificationId notificationId))
            {
                s_notificationsManager.RemoveNotification(notificationId);
                s_transientNotificationsByKey.Remove(key);
            }
        }
    }
}
