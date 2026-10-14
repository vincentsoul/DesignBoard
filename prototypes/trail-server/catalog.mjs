// Original demo balancing data; these quantities are not recovered game rules.
export const catalog = Object.freeze({
  ruleSource: 'prototype-assumptions',
  startingChits: 100,
  maxChits: 1_000_000_000,
  maxStack: 9999,
  townCapacity: 8,
  weeklyReward: { chits: 25, items: { berry: 2 } },
  items: {
    wood: { name: '木材', buyPrice: 2 },
    fiber: { name: '纤维', buyPrice: 2 },
    berry: { name: '浆果', buyPrice: 3, energy: 5 },
    board: { name: '木板', buyPrice: 8 },
    rope: { name: '绳索', buyPrice: 8 },
    ration: { name: '口粮', buyPrice: 10, energy: 20 },
  },
  recipes: {
    board: { inputs: { wood: 2 }, output: { board: 1 } },
    rope: { inputs: { fiber: 2 }, output: { rope: 1 } },
    ration: { inputs: { berry: 2 }, output: { ration: 1 } },
  },
});

export const capabilities = [
  { module: 'session', clientSymbols: ['NetClient.SendPlayerLogin', 'NetClient.SendHeartBeat'], implemented: ['local guest session', 'heartbeat'], assumptions: ['local bearer session; no SDK login validation'] },
  { module: 'player', clientSymbols: ['NetClient.SendCreateCharacterName', 'PlayerInventory'], implemented: ['rename', 'checkpoint persistence'], assumptions: ['checkpoint schema and starter state are demo rules'] },
  { module: 'inventory', clientSymbols: ['NetClient.SendGetInventory', 'XLuaInventoryItf.MoveItemBetweenContainer', 'INET_INVENTORY.Send_DROP_ITEM', 'INET_INVENTORY.Send_C_USE_FUNCTION_ITEM'], implemented: ['backpack/storage', 'move', 'drop', 'use'], assumptions: ['stacked demo items; original equipment UID model is not reproduced'] },
  { module: 'craft', clientSymbols: ['INET_RECIPE.Send_C_RECIPE_LV_UP', 'INET_RECIPE.Send_C_RECIPE_FAMILIARITY', 'PlayerInventory.recipes'], implemented: ['known-recipe crafting'], assumptions: ['crafting costs/yields are demo rules; original request not identified'] },
  { module: 'trade', clientSymbols: ['NetClient.SendTradePlayerSell', 'NetClient.SendTradePlayerBuy', 'NetClient.SendTradeUpdate', 'NetClient.SendTradeOver'], implemented: ['escrow listing', 'buy', 'cancel'], assumptions: ['fixed-price marketplace; original camp auction timing is not reproduced'] },
  { module: 'town', clientSymbols: ['XLuaTownItf.Send_CREATE_OWN_TOWN', 'XLuaTownItf.Send_C_TOWN_RENAME', 'XLuaTownItf.Send_C_CHANGE_TOWN_RULES', 'XLuaTownItf.Send_C_DISSOLVE_TOWN', 'NetClient.SendUpdateBlackboard'], implemented: ['create', 'join', 'leave', 'dissolve', 'mayor update', 'announcement'], assumptions: ['8 plots; join and ownership rules are prototype choices'] },
  { module: 'contribution', clientSymbols: ['XLuaTownItf.Send_C_EXCHANGE_TOWN_FUNDS', 'XLuaTownItf.Send_C_REQUEST_CONTRIBUTION_RANK'], implemented: ['contribute chits', 'town contribution ranking'], assumptions: ['one chit equals one contribution point'] },
  { module: 'rewards', clientSymbols: ['INET_TOWNLANDSCAPE.Send_C_GET_WEEKLY_REWARDS'], implemented: ['one reward per player per Shanghai calendar week'], assumptions: ['reward amount and eligibility are prototype rules'] },
];
