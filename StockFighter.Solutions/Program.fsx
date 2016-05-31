#load "Common.fsx"

open System
open StockFighter.Api
open Common

let start level = async {    
    let logger = createLogger ()
    let! settings = startLevel logger level
    return settings, logger
}

let restart = async {
    let logger = createLogger ()
    let settings = StockFighter.Api.Settings.deserializeSettings(IO.File.ReadAllText(instanceSettingsFile))
    let! response = GamesMaster.restartInstance logger apiKey settings.InstanceId
    //the API docs says settings remain the same, but it doesn't look like it to me, and there is someone else with the same problem in the forum
    let settings = StockFighter.Api.Settings.createSettings apiKey response
    return settings, logger
}

module Level2 = 
    let numberOfBidsAndAsksToFetch = 3

    let getTargetPrice (getQuote: Async<QuoteResponse option>) = async {
        let rec getQuotes (recentQuotes:QuoteResponse list) = async {
            let nAsks = recentQuotes |> List.filter (fun x -> x.Ask > 0) |> List.length
            let nBids = recentQuotes |> List.filter (fun x -> x.Bid > 0) |> List.length

            if nAsks < numberOfBidsAndAsksToFetch || nBids < numberOfBidsAndAsksToFetch then
                let! newQuote = getQuote
                do! Async.Sleep 200
                match newQuote with
                | Some quote -> return! getQuotes (quote :: recentQuotes)
                | _ -> return! getQuotes recentQuotes
            else
                return recentQuotes
        }

        let! recentQuotes = getQuotes []

        let recentAsks = recentQuotes |> Seq.map(fun x -> x.Ask) |> Seq.filter (fun x -> x > 0)
        let recentBids = recentQuotes |> Seq.map(fun x -> x.Bid) |> Seq.filter (fun x -> x > 0)
        let minBidAskSpread = (Seq.min recentAsks) - (Seq.max recentBids)
        return Seq.min recentAsks
    }

    let go alreadyRunning = async {
        let! settings, logger =
            match alreadyRunning with
            | true -> restart
            | false -> start "chock_a_block"

        let getQuote = Stocks.getQuote logger settings.ApiKey settings.Venue settings.Symbol
        let placeOrder = Stocks.placeOrder logger settings.ApiKey settings.Venue settings.Symbol
        let! targetPrice = getTargetPrice getQuote
        let targetQuantity = 100000
        let trancheSize = 2000

        let rec goBuyStuff amountBought = async {
            if amountBought >= targetQuantity then
                ()
            else

            printfn "Placing order"
            let order = {
                Order.Account = settings.Account
                Price = targetPrice
                Qty = trancheSize
                Direction = Buy
                OrderType = IOC
            }            
            let! result = placeOrder order
            printfn "Placed order"
            let newlyBought = result.Fills |> Seq.sumBy (fun x -> x.Qty)
            do! Async.Sleep(500)
            return! goBuyStuff (amountBought + newlyBought)
        }

        do! goBuyStuff 0            
    }

module Level3 =

    type State = {
        CurrentPosition: int
        TotalPendingBuy: int
        TotalPendingSell: int
        CurrentBuy: PlaceOrderResponse option
        CurrentSell: PlaceOrderResponse option
    }

    let go alreadyRunning = async {
        let! settings, logger =
            match alreadyRunning with
            | true -> restart
            | false -> start "sell_side"

        let placeOrder = Stocks.placeOrder logger settings.ApiKey settings.Venue settings.Symbol
        let cancelOrder = Stocks.cancelOrder logger apiKey settings.Venue settings.Symbol
        let repeatOrder = Stocks.repeatOrder logger apiKey settings.Venue settings.Symbol settings.Account

        let maxPosition = 600
        let maxTrancheSize = 200

        let initial = {
            CurrentPosition = 0
            TotalPendingBuy = 0
            TotalPendingSell = 0
            CurrentBuy = None
            CurrentSell = None
        }

        let margin = 80

        let placeOrder (order:Order) state = async {
            let! response = placeOrder order
            let currentBuy, currentSell, newPendingBuys, newPendingSells =
                match order.Direction with
                | Buy -> Some response, state.CurrentSell, state.TotalPendingBuy + order.Qty, state.TotalPendingSell
                | Sell -> state.CurrentBuy, Some response, state.TotalPendingBuy, state.TotalPendingSell + order.Qty
            let newState = { state with
                                TotalPendingBuy = newPendingBuys;
                                TotalPendingSell = newPendingSells;
                                CurrentBuy = currentBuy
                                CurrentSell = currentSell }

            logger (sprintf "placed %A order for %d shares at %d" order.Direction order.Qty order.Price)
            return newState
        }

        let maybeRepeatOrder
            response
            stateIfOrderStillActive
            stateIfRepeatOrder
            currentOrder
            (direction:string) = async {

            let orderStillActive order =
                response.Order.Qty > 0

            let fillSummary (fills:ExecutionTickerResponseFill list) = 
                let totalCount = response.Order.Fills |> Seq.sumBy (fun x -> x.Qty)
                let totalPrice = response.Order.Fills |> Seq.sumBy (fun x -> x.Price * x.Qty)
                let averagePrice = totalPrice / totalCount
                totalCount, averagePrice

            let fills = response.Filled
            let! newState = async {
                if orderStillActive response.Order then
                    return stateIfOrderStillActive
                else
                    match currentOrder with
                    | Some order ->
                        logger (sprintf "repeat %s" direction)
                        let! newOrder = repeatOrder order
                        return stateIfRepeatOrder order
                    | None ->
                        logger "Something has gone wrong, received order completed message when state has no record of the order"
                        return stateIfOrderStillActive }

            let totalCount, averagePrice = fillSummary response.Order.Fills
            logger (sprintf "bought %d average price %d" totalCount averagePrice)
            return newState
        }

        let mailBoxHandler message state = async {
            match message with
            | QuoteTickerResponse response ->
                return state

            | ExecutionTickerResponse response ->                

                if response.Account = settings.Account then
                    match response.Order.Direction with
                    | "buy" ->
                        let newPosition = state.CurrentPosition + response.Filled

                        let stateIfOrderStillActive = 
                            { state with CurrentPosition = newPosition; TotalPendingBuy = response.Order.Qty; }

                        let stateIfRepeatOrder order = 
                            { state with CurrentPosition = newPosition; CurrentBuy = Some order; TotalPendingBuy = order.Qty }

                        return! maybeRepeatOrder
                            response
                            stateIfOrderStillActive
                            stateIfRepeatOrder
                            state.CurrentBuy
                            response.Order.Direction
                    | "sell" ->
                        let newPosition = state.CurrentPosition - response.Filled

                        let stateIfOrderStillActive = 
                            { state with CurrentPosition = newPosition; TotalPendingSell = response.Order.Qty; }

                        let stateIfRepeatOrder order = 
                            { state with CurrentPosition = newPosition; CurrentSell = Some order; TotalPendingBuy = order.Qty }

                        return! maybeRepeatOrder
                            response
                            stateIfOrderStillActive
                            stateIfRepeatOrder
                            state.CurrentSell
                            response.Order.Direction
                    | _ ->
                        logger (sprintf "unrecognised %s" response.Order.Direction)
                        return state
                else
                    return state }

        let mailbox = MailboxProcessor<StockFighterEvent>.Start <| fun inbox -> 
            let rec messageLoop state =
                async {
                    let! message = inbox.Receive()
                    let! newState = mailBoxHandler message state
                    return! messageLoop newState
                }
            messageLoop initial

        StockTicker.create logger settings.Account settings.Venue
            (fun x -> mailbox.Post(QuoteTickerResponse x))
        ExecutionTicker.create logger settings.Account settings.Venue
            (fun x -> mailbox.Post(ExecutionTickerResponse x))
    }

