namespace Content.BoleroSpike.Client

open System
open System.Net.Http
open Microsoft.AspNetCore.Components.WebAssembly.Hosting
open Microsoft.Extensions.DependencyInjection

module Program =

    [<EntryPoint>]
    let Main args =
        let builder = WebAssemblyHostBuilder.CreateDefault(args)
        builder.RootComponents.Add<Main>("#main") |> ignore

        let baseAddress =
            match builder.Configuration["SS14:StatusBaseAddress"] with
            | null
            | "" -> "http://localhost:1212/"
            | value when value.EndsWith("/", StringComparison.Ordinal) -> value
            | value -> value + "/"

        builder.Services.AddScoped(fun _ -> new HttpClient(BaseAddress = Uri baseAddress)) |> ignore

        builder.Build().RunAsync() |> ignore
        0
