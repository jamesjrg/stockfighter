namespace StockFighter.Api

open System
open System.Collections.Generic
open Newtonsoft.Json

type QuoteResponse = {
    Ok: bool
    Symbol: string
    Venue: string
    Bid: int
    Ask: int
    BidSize: int
    AskSize: int
    BidDepth: int
    AskDepth: int
    Last: int
    LastSize: int
    LastTrade: DateTime
    QuoteTime: DateTime
}

type OrderBookResponse = {
    Ok: bool
    Symbol: string
    Venue: string
    Bids: OrderBookOrderInfo list
    Asks: OrderBookOrderInfo list
    Ts: DateTime
}
and OrderBookOrderInfo = {
    Price: int
    Qty: int
    IsBuy: bool
}

type PlaceOrderResponse = {
    Ok: bool
    Symbol: string
    Venue: string
    Direction: string
    OriginalQty: int
    Qty: int
    Price: int
    OrderType: string
    Id: int
    Account: string
    Ts: DateTime
    Fills: PlaceOrderFillInfo list
    TotalFilled: int
    Open: bool
    Error: string
}
and PlaceOrderFillInfo = {
    Price: int
    Qty: int
    Ts: DateTime
}

type PlaceOrderRequest = {
    Account: string
    Price: int
    Qty: int
    Direction: string
    OrderType: string
}

type StartGameResponse = {
    Account: string
    InstanceId: int
    Instructions: StartGameInstructions
    Ok: bool
    SecondsPerTradingDay: int 
    Tickers: string list
    Venues: string list
    Balances: Dictionary<string, int>    
}
and StartGameInstructions = {
    Instructions: string
    OrderTypes: string //this would need some custom logic to deserialize as Newtonsoft doesn't have any simple way to handle spaces in property names
}

type QuoteTickerResponse = {
    Ok: bool
    Quote: QuoteTickerResponseQuote
}
and QuoteTickerResponseQuote = {
    Symbol: string
    Venue: string
    Bid: int
    Ask: int
    BidSize: int
    AskSize: int
    BidDepth: int
    AskDepth: int
    Last: int
    LastSize: int
    LastTrade: DateTime
    QuoteTime: DateTime
}

type ExecutionTickerResponse = {
    Ok: bool
    Account:  string
    Venue: string
    Symbol: string
    Order: ExecutionTickerResponseOrder
    StandingId: int
    IncomingId: int
    Price: int
    Filled: int
    FilledAt: DateTime
    StandingComplete: bool  // whether the order that was on the book is now complete
    IncomingComplete: bool  // whether the incoming order is complete (as of this execution)
}
and ExecutionTickerResponseOrder = {
    Ok:  bool
    Symbol: string
    Venue: string
    Direction: string
    OriginalQty: int
    Qty: int
    Price: int
    OrderType: string
    Id: int
    Account: string
    Ts: DateTime
    Fills: ExecutionTickerResponseFill list
    TotalFilled: int
    Open: bool
}
and ExecutionTickerResponseFill = {
    Price: int
    Qty: int
    Ts: DateTime
}