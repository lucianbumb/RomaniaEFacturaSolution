# Quick test script to validate the library is working
cd d:\Work\Projects\RomaniaEFacturaLibrary\RomaniaEFacturaConsole

# Run console app with option 1 (validate invoice) and then exit
$input = "1`n0`n"
$input | dotnet run

Write-Host "Library validation complete!"
