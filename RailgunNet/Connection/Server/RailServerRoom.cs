﻿/*
 *  RailgunNet - A Client/Server Network State-Synchronization Layer for Games
 *  Copyright (c) 2016-2018 - Alexander Shoulson - http://ashoulson.com
 *
 *  This software is provided 'as-is', without any express or implied
 *  warranty. In no event will the authors be held liable for any damages
 *  arising from the use of this software.
 *  Permission is granted to anyone to use this software for any purpose,
 *  including commercial applications, and to alter it and redistribute it
 *  freely, subject to the following restrictions:
 *  
 *  1. The origin of this software must not be misrepresented; you must not
 *     claim that you wrote the original software. If you use this software
 *     in a product, an acknowledgment in the product documentation would be
 *     appreciated but is not required.
 *  2. Altered source versions must be plainly marked as such, and must not be
 *     misrepresented as being the original software.
 *  3. This notice may not be removed or altered from any source distribution.
 */

using System;
using System.Collections.Generic;
using RailgunNet.Factory;
using RailgunNet.Logic;
using RailgunNet.System.Types;
using RailgunNet.Util;

namespace RailgunNet.Connection.Server
{
    [OnlyIn(Component.Server)]
    public class RailServerRoom : RailRoom
    {
        /// <summary>
        ///     All client controllers involved in this room.
        ///     Does not include the server's controller.
        /// </summary>
        private readonly HashSet<RailPeer> clients;

        /// <summary>
        ///     The local Railgun server.
        /// </summary>
        private readonly RailServer server;

        /// <summary>
        ///     Used for creating new entities and assigning them unique ids.
        /// </summary>
        private EntityId nextEntityId = EntityId.START;

        public RailServerRoom(RailResource resource, RailServer server) : base(resource, server)
        {
            ToUpdate = new List<RailEntityServer>();
            ToRemove = new List<RailEntityServer>();

            clients = new HashSet<RailPeer>();
            this.server = server;
        }

        private List<RailEntityServer> ToRemove { get; } // Pre-allocated removal list
        private List<RailEntityServer> ToUpdate { get; } // Pre-allocated update list

        /// <summary>
        ///     Fired when a controller has been added (i.e. player join).
        ///     The controller has control of no entities at this point.
        /// </summary>
        public event Action<RailController> ClientJoined;

        /// <summary>
        ///     Fired when a controller has been removed (i.e. player leave).
        ///     This event fires before the controller has control of its entities
        ///     revoked (this is done immediately afterwards).
        /// </summary>
        public event Action<RailController> ClientLeft;

        /// <summary>
        ///     Adds an entity to the room. Cannot be done during the update pass.
        /// </summary>
        public T AddNewEntity<T>()
            where T : RailEntityServer
        {
            T entity = CreateEntity<T>();
            RegisterEntity(entity);
            return entity;
        }

        /// <summary>
        ///     Marks an entity for removal from the room and presumably destruction.
        ///     This is deferred until the next frame.
        /// </summary>
        public void MarkForRemoval(RailEntityBase entity)
        {
            if (entity.IsRemoving == false)
            {
                RailEntityServer serverEntity = entity as RailEntityServer;
                if (serverEntity == null)
                {
                    throw new ArgumentNullException(
                        nameof(entity),
                        $"unexpected type of entity to remove: {entity}");
                }

                serverEntity.MarkForRemoval();
                server.LogRemovedEntity(serverEntity);
            }
        }

        /// <summary>
        ///     Queues an event to broadcast to all present clients.
        /// </summary>
        public void BroadcastEvent(RailEvent evnt, ushort attempts = 3, bool freeWhenDone = true)
        {
            foreach (RailPeer client in clients)
            {
                client.SendEvent(evnt, attempts);
            }

            if (freeWhenDone) evnt.Free();
        }

        public void AddClient(RailPeer client)
        {
            clients.Add(client);
            OnClientJoined(client);
        }

        public void RemoveClient(RailPeer client)
        {
            clients.Remove(client);
            OnClientLeft(client);
        }

        public void ServerUpdate()
        {
            Tick = Tick.GetNext();
            OnPreRoomUpdate(Tick);

            // Collect the entities in the priority order and
            // separate them out for either update or removal
            foreach (RailEntityServer entity in GetAllEntities<RailEntityServer>())
            {
                if (entity.ShouldRemove)
                {
                    ToRemove.Add(entity);
                }
                else
                {
                    ToUpdate.Add(entity);
                }
            }

            // Wave 0: Remove all sunsetted entities
            ToRemove.ForEach(RemoveEntity);

            // Wave 1: Start/initialize all entities
            ToUpdate.ForEach(e => e.PreUpdate());

            // Wave 2: Update all entities
            ToUpdate.ForEach(e => e.ServerUpdate());

            // Wave 3: Post-update all entities
            ToUpdate.ForEach(e => e.PostUpdate());

            ToRemove.Clear();
            ToUpdate.Clear();
            OnPostRoomUpdate(Tick);
        }

        public void StoreStates()
        {
            foreach (RailEntityServer entity in Entities)
            {
                entity.StoreRecord(Resource);
            }
        }

        private T CreateEntity<T>()
            where T : RailEntityServer
        {
            T entity = RailEntityServer.Create<T>(Resource);
            entity.AssignId(nextEntityId);
            nextEntityId = nextEntityId.GetNext();
            return entity;
        }

        private void OnClientJoined(RailController client)
        {
            ClientJoined?.Invoke(client);
        }

        private void OnClientLeft(RailController client)
        {
            ClientLeft?.Invoke(client);
        }
    }
}
