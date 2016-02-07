namespace StockFighter.Api

open StockFighter.Api.WebSockets

open HttpFs.Client
open Newtonsoft.Json
open System

module Http =
    JsonConvert.DefaultSettings <- new System.Func<JsonSerializerSettings>(fun () ->
        new JsonSerializerSettings(ContractResolver = new Serialization.CamelCasePropertyNamesContractResolver()))

    let sendRequest logger apiKey url verb data = async {
        let request =
            createRequest verb <| Uri(url)
            |> withHeader (ContentType (ContentType.Parse "application/json" |> Option.get))
            |> withHeader (Custom ("X-Starfighter-Authorization", apiKey))

        let request =
            match data with
            | Some data ->
                let serialized = JsonConvert.SerializeObject(data)
                request |> withBodyString serialized
            | _ -> request

        use! response = getResponse request
        let! bodyStr = Response.readBodyAsString response
        if response.StatusCode <> 200 then
            //maybe don't do this for created, deleted etc
            logger (sprintf "Non 200 response on %O to %s: %d, %s" verb url response.StatusCode bodyStr)
        return bodyStr
    }

module GamesMaster =
    let gamesMasterUrl =
        "https://www.stockfighter.io/gm"

    let startLevel logger apiKey level = async {
        logger (sprintf "Starting level %s" level)
        let url = sprintf "%s/levels/%s" gamesMasterUrl level
        let! json = Http.sendRequest logger apiKey url Post None
        logger "Started"
        return JsonConvert.DeserializeObject<StartGameResponse>(json)
    }

    let restartInstance logger apiKey instanceId = async {
        logger "Restarting"
        let url = sprintf "%s/instances/%d/restart" gamesMasterUrl instanceId
        let! json = Http.sendRequest logger apiKey url Post None
        logger "Restarted"
        return JsonConvert.DeserializeObject<StartGameResponse>(json)
    }

module WebSocketsAgent =
    let create logger wsUri onSuccess =
        let mailbox = MailboxProcessor<ReceiveResult>.Start <| fun inbox -> 
                let rec messageLoop () =
                    async {
                        let! message = inbox.Receive()
                        match message with
                        | Success str ->
                            onSuccess str
                            return! messageLoop()
                        | Error str ->
                            logger (sprintf "WebSocket error for %s: %s" wsUri str)
                            return ()
                    }
                messageLoop ()

        let startTask = WebSockets.startSocket logger (Uri(wsUri)) (fun msg -> mailbox.Post(msg))
        startTask |> Async.StartAsTask |> ignore

module StockTicker =
    let deserialize logger onSuccess json =
        try
            let response = JsonConvert.DeserializeObject<QuoteTickerResponse>(json)
            onSuccess response
        with
        | exn -> logger (sprintf "Exn: %O" exn)

    let create logger account venue onSuccess = 
        let wsUri = sprintf "wss://api.stockfighter.io/ob/api/ws/%s/venues/%s/tickertape" account venue
        WebSocketsAgent.create logger wsUri (deserialize logger onSuccess)

module ExecutionTicker =
    let deserialize logger onSuccess json =
        try
            let response = JsonConvert.DeserializeObject<ExecutionTickerResponse>(json)
            onSuccess response
        with
        | exn -> logger (sprintf "Exn: %O" exn)

    let create logger account venue onSuccess = 
        let wsUri = sprintf "wss://api.stockfighter.io/ob/api/ws/%s/venues/%s/executions" account venue
        WebSocketsAgent.create logger wsUri (deserialize logger onSuccess)       

module Stocks = 
    let sharedStockUrlPrefix venue stock =
        let baseUrl =  "https://api.stockfighter.io/ob/api"
        sprintf "%s/venues/%s/stocks/%s" baseUrl venue stock

    let orderTypeForRequest = function
        | Limit -> "limit"
        | Market -> "market"
        | FOK -> "fill-or-kill"
        | IOC -> "immediate-or-cancel"

    let orderDirectionForRequest = function
        | Buy -> "buy"
        | Sell -> "sell"

    let orderTypeFromRequest = function
        | "limit" -> Some Limit
        | "market" -> Some Market
        | "fill-or-kill" -> Some FOK
        | "immediate-or-cancel" -> Some IOC
        | _ -> None

    let orderDirectionFromRequest = function
        | "buy" -> Some Buy
        | "sell" -> Some Sell
        | _ -> None

    let getQuote logger apiKey venue stock = async {
        let tryGet = async {
            let url = sprintf "%s/quote" (sharedStockUrlPrefix venue stock)
            let! json = Http.sendRequest logger apiKey url Get None
            return JsonConvert.DeserializeObject<QuoteResponse>(json);
        } 
        let! maybeResult = tryGet |> Async.Catch
        return 
            match maybeResult with
            | Choice1Of2 response -> Some response
            | Choice2Of2 exn -> logger (sprintf "%O" exn); None
    }

    let getOrderBook logger apiKey venue stock = async {
        let url = sharedStockUrlPrefix venue stock
        let! json = Http.sendRequest logger apiKey url Get None
        let orderBookResponse = JsonConvert.DeserializeObject<OrderBookResponse>(json);
        return orderBookResponse        
    }

    let mapOrderToRequest (order:Order) = 
        {
            PlaceOrderRequest.Account = order.Account
            Price = order.Price
            Qty = order.Qty
            Direction = orderDirectionForRequest order.Direction
            OrderType = orderTypeForRequest order.OrderType
        }

    let placeOrder logger apiKey venue stock (order:Order) = async {
        let url = sprintf "%s/orders" (sharedStockUrlPrefix venue stock)
        let request = mapOrderToRequest order
        let! json = Http.sendRequest logger apiKey url Post (Some request)
        let orderResponse = JsonConvert.DeserializeObject<PlaceOrderResponse>(json);
        return orderResponse
    }

    let repeatOrder logger apiKey venue stock account (orderResponse:PlaceOrderResponse) = async {
        let order =
            {
                Order.Account = account
                Price = orderResponse.Price
                Qty = orderResponse.Qty
                //should do actual error handling
                Direction = defaultArg (orderDirectionFromRequest orderResponse.Direction) Buy
                OrderType = defaultArg (orderTypeFromRequest orderResponse.OrderType) Limit
            }

        return! placeOrder logger apiKey venue stock order
    }

    let cancelOrder logger apiKey venue stock orderId =
        let url = sprintf "%s/orders/%d" (sharedStockUrlPrefix venue stock) orderId
        Http.sendRequest logger apiKey url Delete None

