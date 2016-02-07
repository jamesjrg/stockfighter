namespace StockFighter.Api

open System

type Symbol = string

type OrderType =
    | Limit
    | Market
    | FOK
    | IOC

type OrderDirection =
    | Buy
    | Sell

