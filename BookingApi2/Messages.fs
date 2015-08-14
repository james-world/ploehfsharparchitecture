namespace BookingApi2

open System

[<CLIMutable>]
type MakeReservation = {
    Date : DateTime
    Name : string
    Email : string
    Quantity : int }

[<AutoOpen>]
module Envelope =

    [<CLIMutable>]
    type Envelope<'a> = {
        Id : Guid
        Created : DateTime
        Item : 'a }

    let Envelop id created item = {
        Id = id
        Created = created
        Item = item }

    let EnvelopWithDefaults item =
        Envelop (Guid.NewGuid()) (DateTime.UtcNow) item

[<CLIMutable>]
type Reservation = {
    Date : DateTime
    Name : string
    Email : string
    Quantity : int }

[<CLIMutable>]
type Notification = {
    About : Guid
    Type : string
    Message : string }