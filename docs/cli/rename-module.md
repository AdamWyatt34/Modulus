# Renaming a Module (Manual Procedure)

There is deliberately no `modulus rename-module` command yet: a rename touches namespaces, project names, folder paths, solution entries, cross-module references, and source-generated registration names, and a half-applied rename leaves a solution that neither builds nor cleanly reverts. Until a transactional implementation ships, rename manually with the checklist below.

Work on a clean branch so a failed attempt is one `git reset --hard` away.

## Checklist: `Orders` → `Sales`

1. **Rename the module folders**

   ```powershell
   git mv src/Modules/Orders src/Modules/Sales
   ```

2. **Rename each project folder and csproj** under `src/Modules/Sales/src` and `.../tests`:

   ```powershell
   git mv src/Modules/Sales/src/Orders.Domain src/Modules/Sales/src/Sales.Domain
   git mv src/Modules/Sales/src/Sales.Domain/Orders.Domain.csproj src/Modules/Sales/src/Sales.Domain/Sales.Domain.csproj
   # ...repeat for Application, Infrastructure, Integration, and the test projects
   ```

3. **Update the solution file** (`.slnx`): replace every `Orders.` project path with `Sales.`.

4. **Update ProjectReferences**: search the whole solution for `Orders.Domain.csproj`, `Orders.Integration.csproj`, etc. Cross-module consumers reference your Integration project — those csprojs live in *other* modules.

   ```powershell
   Get-ChildItem -Recurse -Filter *.csproj | Select-String "Orders\."
   ```

5. **Rename namespaces and type names**: solution-wide find/replace of `Orders.Domain` → `Sales.Domain` (and the other layers), plus the module registration class (`OrdersModule` → `SalesModule` or equivalent `IModuleRegistration` implementation — the module discovery generator picks the new name up automatically on rebuild).

6. **Update configuration**: `modules.orders.json` (if you use per-module config), any `Messaging` assemblies lists, and appsettings keys that embed the module name.

7. **Messaging caution — integration events**: renaming the Integration *namespace* changes each event's wire type name (topics/exchanges derive from the full type name). If other deployments consume these events, treat this as a [drain-before-upgrade](/messaging/migrating-from-masstransit#upgrade-procedure-drain-before-upgrade) change, and expect new broker topology (old exchanges/topics remain and can be deleted). The inbox/outbox tables store assembly-qualified type names — drain the outbox before renaming (`modulus outbox list-failed` should be empty and no rows pending).

8. **Verify**:

   ```powershell
   dotnet build
   modulus doctor
   modulus list-modules
   dotnet test
   ```

## See Also

- [`modulus remove-module`](./remove-module) — when deleting and re-adding is simpler than renaming
- [`modulus doctor`](./doctor) — catches dangling references after the rename
