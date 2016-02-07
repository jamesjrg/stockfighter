namespace StockFighter.Api

open StockFighter.Api.Settings
open StockFighter.Api.WebSockets

open HttpFs.Client
open Newtonsoft.Json
open System

module Http =
    JsonConvert.DefaultSettings <- new System.Func<JsonSerializerSettings>(fun () ->
        new JsonSerializerSettings(ContractResolver = new Serialization.CamelCasePropertyNamesContractResolver()))

    let sendRequest apiKey url verb data = async {
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

        printfn "Sending %A request to %s" verb url
        use! response = getResponse request
        let! bodyStr = Response.readBodyAsString response
        if response.StatusCode <> 200 then
            //maybe don't do this for created, deleted etc
            printfn "Non 200 response: %s" bodyStr
        return bodyStr
    }

module GamesMaster =
    let gamesMasterUrl =
        "https://www.stockfighter.io/gm"

    let startLevel apiKey level = async {
        let url = sprintf "%s/levels/%s" gamesMasterUrl level
        let! json = Http.sendRequest apiKey url Post None
        printfn "Output: %s" json
        let startGameResponse = JsonConvert.DeserializeObject<StartGameResponse>(json)
        return startGameResponse
    }

    let restartInstance apiKey instanceId = async {
        let url = sprintf "%s/instances/%d/restart" gamesMasterUrl instanceId
        let! json = Http.sendRequest apiKey url Post None
        return json
    }

module StockTicker =

module Stocks = 
    let sharedStockUrlPrefix venue stock =
        let baseUrl =  "https://api.stockfighter.io/ob/api"
        sprintf "%s/venues/%s/stocks/%s" baseUrl venue stock

    (*let stockCatalogue =
        [
            {Symbol = "settings.Symbol"}
        ]
        |> Seq.map (fun f -> f.Symbol, f)
        |> Map.ofSeq*)

    let orderTypeForRequest = function
        | Limit -> "limit"
        | Market -> "market"
        | FOK -> "fill-or-kill"
        | IOC -> "immediate-or-cancel"

    let orderDirectionForRequest = function
        | Buy -> "buy"
        | Sell -> "sell"

    let getQuote apiKey venue stock = async {
        let tryGet = async {
            let url = sprintf "%s/quote" (sharedStockUrlPrefix venue stock)
            let! json = Http.sendRequest apiKey url Get None
            return JsonConvert.DeserializeObject<QuoteResponse>(json);
        } 
        let! maybeResult = tryGet |> Async.Catch
        return 
            match maybeResult with
            | Choice1Of2 response -> Some response
            | Choice2Of2 exn -> printfn "%O" exn; None
    }

    let getOrderBook apiKey venue stock = async {
        let url = sharedStockUrlPrefix venue stock
        let! json = Http.sendRequest apiKey url Get None
        let orderBookResponse = JsonConvert.DeserializeObject<OrderBookResponse>(json);
        return orderBookResponse        
    }

    let placeOrder apiKey account venue stock price qty direction orderType = async {
        let url = sprintf "%s/orders" (sharedStockUrlPrefix venue stock)
        let data = {
            PlaceOrderRequest.Account = account
            Price = price
            Qty = qty
            Direction = orderDirectionForRequest direction
            OrderType = orderTypeForRequest orderType
        }
        let! json = Http.sendRequest apiKey url Post (Some data)
        let orderResponse = JsonConvert.DeserializeObject<PlaceOrderResponse>(json);
        return orderResponse
    }

    let cancelOrder apiKey venue stock orderId =
        let url = sprintf "%s/orders/%s" (sharedStockUrlPrefix venue stock) orderId
        Http.sendRequest apiKey url Delete None

