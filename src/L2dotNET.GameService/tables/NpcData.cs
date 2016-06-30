﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml.Linq;
using log4net;
using L2dotNET.GameService.Model.Items;
using L2dotNET.GameService.Model.Npcs;
using L2dotNET.GameService.Model.Player;
using L2dotNET.GameService.Network;
using L2dotNET.GameService.Network.Serverpackets;
using L2dotNET.GameService.Tables.Admin_Bypass;
using L2dotNET.GameService.Tables.Ndextend;

namespace L2dotNET.GameService.Tables
{
    class NpcData
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(NpcData));
        private static volatile NpcData instance;
        private static readonly object syncRoot = new object();

        public static NpcData Instance
        {
            get
            {
                if (instance == null)
                    lock (syncRoot)
                    {
                        if (instance == null)
                            instance = new NpcData();
                    }

                return instance;
            }
        }

        public void Initialize()
        {
            load();
        }

        public SortedList<int, ND_shop> _shops = new SortedList<int, ND_shop>();
        private NDTeleport Teleports;

        public NpcData() { }

        private void load()
        {
            _shops = new SortedList<int, ND_shop>();

            Teleports = new NDTeleport();

            ItemTable itable = ItemTable.Instance;
            {
                XElement xml = XElement.Parse(File.ReadAllText(@"scripts\buylists.xml"));
                foreach (XElement shops in xml.Elements("shops"))
                    foreach (XElement shopp in shops.Elements("shop"))
                    {
                        ND_shop shop = new ND_shop();
                        XElement npcElement = shopp.Element("npc");
                        if (npcElement != null)
                            shop.id = int.Parse(npcElement.Value);
                        XElement modElement = shopp.Element("mod");
                        if (modElement != null)
                            shop.mod = double.Parse(modElement.Value);

                        foreach (XElement selllist in shopp.Elements("selllist"))
                        {
                            ND_shopList slist = new ND_shopList();
                            slist.id = short.Parse(selllist.Attribute("id").Value);

                            XElement itemElement = selllist.Element("item");
                            if (itemElement != null)
                            {
                                string items = itemElement.Value;
                                items = items.Replace("\n", "").Replace(" ", "");

                                foreach (string i in items.Split(','))
                                {
                                    ItemTemplate it = itable.GetItem(Convert.ToInt32(i));
                                    if (it != null)
                                        slist.items.Add(new ND_shopItem(it));
                                    else
                                        log.Error($"NpcData: cant find item to trade {i} on npc {shop.id}");
                                }
                            }

                            shop.lists.Add(slist.id, slist);
                        }

                        _shops.Add(shop.id, shop);
                    }
            }

            log.Info("NpcData: loaded " + _shops.Count + " merchants.");
            //CLogger.info("NpcData: loaded " + _mults.Count + " multisell lists.");
        }

        public void Buylist(L2Player player, L2Npc trader, short reply)
        {
            if (!_shops.ContainsKey(trader.Template.NpcId))
            {
                player.sendMessage("you shop was not found");
                player.sendActionFailed();
                return;
            }

            ND_shop shop = _shops[trader.Template.NpcId];
            GameServerNetworkPacket pk;
            if (!shop.lists.ContainsKey(reply))
            {
                reply -= 2; // примерка

                if (!shop.lists.ContainsKey(reply))
                {
                    player.sendMessage("your shop id was just wrong " + reply);
                    player.sendActionFailed();
                }
                else
                    pk = new ShopPreviewList(player, shop.lists[reply], reply);
            }
            else
            {
                player.sendPacket(new ExBuySellList_Buy(player, shop.lists[reply], 1.10, 1.0, reply));
                player.sendPacket(new ExBuySellList_Sell(player));
            }
        }

        public void RequestTeleportList(L2Npc npc, L2Player player, int groupId)
        {
            RequestTeleportList(npc, player, groupId, -1);
        }

        public void RequestTeleportList(L2Npc npc, L2Player player, int groupId, int itemId)
        {
            if (!Teleports.npcs.ContainsKey(npc.Template.NpcId))
            {
                player.ShowHtmPlain("no teleports available for you", npc);
                player.sendActionFailed();
                return;
            }

            ab_teleport_group group = Teleports.npcs[npc.Template.NpcId].groups[groupId];
            StringBuilder sb = new StringBuilder("&$556;<br><br>");
            foreach (ab_teleport_entry e in group._teles.Values)
            {
                string cost = "";
                int id = itemId != -1 ? itemId : e.itemId;
                if (player.Level >= 40)
                    cost = " - " + e.cost + " &#" + id + ";";

                sb.Append("<a action=\"bypass -h teleport_next?ask=" + groupId + "&reply=" + e.id + "\" msg=\"811;" + e.name + "\">" + e.name + "" + cost + "</a><br1>");
            }

            player.TeleportPayID = itemId;
            player.ShowHtmPlain(sb.ToString(), npc);
        }

        public void RequestTeleport(L2Npc npc, L2Player player, int type, int entryId)
        {
            ab_teleport_group group;
            try
            {
                group = Teleports.npcs[npc.Template.NpcId].groups[type];
            }
            catch
            {
                log.Error($"ND:RequestTeleport cant find teleport group {type}");
                player.sendActionFailed();
                return;
            }

            ab_teleport_entry e = group._teles[entryId];

            if (!player.hasItem(e.itemId, e.cost))
            {
                switch (e.itemId)
                {
                    case 57:
                        player.sendSystemMessage(SystemMessage.SystemMessageId.YOU_NOT_ENOUGH_ADENA);
                        break;
                    case 6651:
                        player.ShowHtm("fornonoblessitem.htm", npc);
                        break;

                    default:
                        player.sendSystemMessage(SystemMessage.SystemMessageId.NOT_ENOUGH_REQUIRED_ITEMS);
                        break;
                }

                player.sendActionFailed();
                return;
            }

            switch (e.itemId)
            {
                case 57:
                    player.reduceAdena(e.cost, true, true);
                    break;

                default:
                    player.Inventory.destroyItem(e.itemId, e.cost, true, true);
                    break;
            }
        }

        internal void preview(L2Player talker, L2Npc myself, int p)
        {
            throw new NotImplementedException();
        }
    }
}