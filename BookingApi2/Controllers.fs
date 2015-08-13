namespace BookingApi2

open System.Web.Http
open System.Net

type HomeController() =
    inherit ApiController()

    [<Route("home")>]
    member this.Get() : IHttpActionResult =
        this.Ok() :> _