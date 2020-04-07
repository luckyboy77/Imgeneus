﻿using Imgeneus.Core.DependencyInjection;
using Imgeneus.Database;
using Imgeneus.Database.Constants;
using Imgeneus.Database.Entities;
using Imgeneus.Network;
using Imgeneus.Network.Data;
using Imgeneus.Network.Packets;
using Imgeneus.Network.Packets.Game;
using Imgeneus.World.Packets;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Imgeneus.World.Game;

namespace Imgeneus.World.Handlers
{
    internal static partial class WorldHandler
    {
        [PacketHandler(PacketType.GAME_HANDSHAKE)]
        public static void OnGameHandshake(WorldClient client, IPacketStream packet)
        {
            var handshake = new HandshakePacket(packet);

            client.SetClientUserID(handshake.UserId);

            using var sendPacket = new Packet(PacketType.GAME_HANDSHAKE);

            sendPacket.Write(0);
            client.SendPacket(sendPacket);

            using var database = DependencyContainer.Instance.Resolve<IDatabase>();

            DbUser user = database.Users.Include(u => u.Characters)
                                        .ThenInclude(c => c.Items)
                                        .Where(u => u.Id == handshake.UserId)
                                        .FirstOrDefault();

            WorldPacketFactory.SendCharacterList(client, user.Characters);
            WorldPacketFactory.SendAccountFaction(client, user);
        }

        [PacketHandler(PacketType.PING)]
        public static void OnPing(WorldClient client, IPacketStream packet)
        {
            using var sendPacket = new Packet(PacketType.PING);
            sendPacket.Write(0);
            client.SendPacket(sendPacket);
        }

        [PacketHandler(PacketType.ACCOUNT_FACTION)]
        public static async void OnAccountFraction(WorldClient client, IPacketStream packet)
        {
            var accountFractionPacket = new AccountFractionPacket(packet);

            using var database = DependencyContainer.Instance.Resolve<IDatabase>();
            DbUser user = database.Users.Find(client.UserID);
            user.Faction = accountFractionPacket.Fraction;

            await database.SaveChangesAsync();
        }

        [PacketHandler(PacketType.CHECK_CHARACTER_AVAILABLE_NAME)]
        public static void OnCheckAvailableName(WorldClient client, IPacketStream packet)
        {
            var checkNamePacket = new CheckCharacterAvailableNamePacket(packet);

            using var database = DependencyContainer.Instance.Resolve<IDatabase>();
            DbCharacter character = database.Characters.FirstOrDefault(c => c.Name == checkNamePacket.CharacterName);

            WorldPacketFactory.SendCharacterAvailability(client, character is null);
        }

        [PacketHandler(PacketType.CREATE_CHARACTER)]
        public static async void OnCreateCharacter(WorldClient client, IPacketStream packet)
        {
            var createCharacterPacket = new CreateCharacterPacket(packet);
            using var database = DependencyContainer.Instance.Resolve<IDatabase>();

            // Get number of user characters.
            var characters = database.Characters.Where(x => x.UserId == client.UserID).ToList();

            if (characters.Count == Constants.MaxCharacters - 1)
            {
                // Max number is reached.
                WorldPacketFactory.SendCreatedCharacter(client, false);
                return;
            }

            byte freeSlot = 0;
            for (byte i = 0; i < Constants.MaxCharacters; i++)
            {
                if (!characters.Any(c => c.Slot == i))
                {
                    freeSlot = i;
                    break;
                }
            }
            DbCharacter character = new DbCharacter()
            {
                Name = createCharacterPacket.CharacterName,
                Race = createCharacterPacket.Race,
                Mode = createCharacterPacket.Mode,
                Hair = createCharacterPacket.Hair,
                Face = createCharacterPacket.Face,
                Height = createCharacterPacket.Height,
                Class = createCharacterPacket.Class,
                Gender = createCharacterPacket.Gender,
                Level = 1,
                Slot = freeSlot,
                UserId = client.UserID
            };

            await database.Characters.AddAsync(character);
            if (await database.SaveChangesAsync() > 0)
            {
                characters.Add(character);
                WorldPacketFactory.SendCreatedCharacter(client, true);
                WorldPacketFactory.SendCharacterList(client, characters);
            }
        }

        [PacketHandler(PacketType.SELECT_CHARACTER)]
        public static void OnSelectCharacter(WorldClient client, IPacketStream packet)
        {
            var selectCharacterPacket = new SelectCharacterPacket(packet);
            var gameWorld = DependencyContainer.Instance.Resolve<IGameWorld>();
            var player = gameWorld.LoadPlayer(selectCharacterPacket.CharacterId);

            WorldPacketFactory.SendSelectedCharacter(client, player);
        }

        [PacketHandler(PacketType.LEARN_NEW_SKILL)]
        public static async void OnNewSkillLearn(WorldClient client, IPacketStream packet)
        {
            var learnNewSkillsPacket = new LearnNewSkillPacket(packet);
            var gameWorld = DependencyContainer.Instance.Resolve<IGameWorld>();
            var player = gameWorld.Players.FirstOrDefault(p => p.Id == client.CharID);
            if (player is null)
            {
                // Not sure if it's really possible... Player should not be null.
                return;
            }
            var successful = await player.LearnNewSkill(learnNewSkillsPacket.SkillId, learnNewSkillsPacket.SkillLevel);
            WorldPacketFactory.LearnedNewSkill(client, player, successful);
        }

        [PacketHandler(PacketType.INVENTORY_MOVE_ITEM)]
        public static async void OnMoveItem(WorldClient client, IPacketStream packet)
        {
            var moveItemPacket = new MoveItemInInventoryPacket(packet);
            var gameWorld = DependencyContainer.Instance.Resolve<IGameWorld>();
            var player = gameWorld.Players.FirstOrDefault(p => p.Id == client.CharID);
            if (player is null)
            {
                // Not sure if it's really possible... Player should not be null.
                return;
            }

            var items = await player.MoveItem(moveItemPacket.CurrentBag, moveItemPacket.CurrentSlot, moveItemPacket.DestinationBag, moveItemPacket.DestinationSlot);
            WorldPacketFactory.SendMoveItem(client, items.sourceItem, items.destinationItem);

            if (items.sourceItem.Bag == 0 || items.destinationItem.Bag == 0)
            {
                // Send equipment update to character.
                WorldPacketFactory.SendEquipment(client, client.CharID, items.sourceItem.Bag == 0 ? items.sourceItem : items.destinationItem);

                // TODO: send equipment update to all characters nearby.
            }
        }

        [PacketHandler(PacketType.CHARACTER_MOVE)]
        public static async void OnMoveCharacter(WorldClient client, IPacketStream packet)
        {
            var movePacket = new MoveCharacterPacket(packet);
            var gameWorld = DependencyContainer.Instance.Resolve<IGameWorld>();
            var player = gameWorld.Players.FirstOrDefault(p => p.Id == client.CharID);
            if (player is null)
            {
                // Not sure if it's really possible... Player should not be null.
                return;
            }

            await player.Move(movePacket.MovementType, movePacket.X, movePacket.Y, movePacket.Z, movePacket.Angle);
        }

        [PacketHandler(PacketType.CHARACTER_ENTERED_MAP)]
        public static void OnCharacterEnteredMap(WorldClient client, IPacketStream packet)
        {
            var gameWorld = DependencyContainer.Instance.Resolve<IGameWorld>();
            gameWorld.LoadPlayerInMap(client.CharID);

            // Send newly connected player all connected players.
            foreach (var player in gameWorld.Players.Where(c => c.Id != client.CharID))
            {
                WorldPacketFactory.CharacterConnectedToMap(client, player);
            }
        }
    }
}
