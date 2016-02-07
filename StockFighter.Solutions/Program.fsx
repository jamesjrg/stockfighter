#load "Common.fsx"

open System
open StockFighter.Api
open Common

let mutable settings = None

let start level = async {    
    let! newSettings = startLevel level    
    settings <- Some newSettings
}

module Level2 = 
    let getTargetPrice (getQuote: Async<QuoteResponse option>) = async {
        let getQuoteThenSleep () = async {
            let! maybeQuote = getQuote
            do! Async.Sleep 200
            return maybeQuote
        }

        let mutable recentQuotes = []
    
        for i in [1..20] do 
            printfn "Getting quote"
            let! newQuote = getQuoteThenSleep ()
            printfn "Got quote"
            if newQuote.IsSome then
                recentQuotes <- newQuote.Value :: recentQuotes

        let recentBids = recentQuotes |> Seq.map (fun x -> x.Bid) |> Seq.filter (fun x -> x > 0)
        let recentAsks = recentQuotes |> Seq.map (fun x -> x.Ask) |> Seq.filter (fun x -> x > 0)

        if Seq.isEmpty recentBids then failwith "Got no bids greater than 0"
        if Seq.isEmpty recentAsks then failwith "Got no ask greater than 0"
        
        let bidAskSpread = Math.Abs ((Seq.min recentAsks) - (Seq.max recentBids))
        return (Seq.min recentBids) + (bidAskSpread / 2)
    }

    let go = async {
        if settings.IsNone then
            do! start "chock_a_block"

        let settings = settings.Value
        let getQuote = Stocks.getQuote settings.ApiKey settings.Venue settings.Symbol
        let placeOrder = Stocks.placeOrder settings.ApiKey settings.Account settings.Venue settings.Symbol
        let! targetPrice = getTargetPrice getQuote
        let targetQuantity = 100000
        let trancheSize = 1000
        let mutable amountBought = 0

        while amountBought < targetQuantity do
            printfn "Placing order"
            let! result = placeOrder targetPrice trancheSize Buy IOC
            printfn "Placed order"
            let newlyBought = result.Fills |> Seq.sumBy (fun x -> x.Qty)
            amountBought <- amountBought + newlyBought
            do! Async.Sleep(100)
    }

