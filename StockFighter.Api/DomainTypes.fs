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

type StockFighterEvent =
    | ExecutionTickerResponse of ExecutionTickerResponse
    | QuoteTickerResponse of QuoteTickerResponse

type Order = {
    Account: string
    Price: int
    Qty: int
    Direction: OrderDirection
    OrderType: OrderType
}