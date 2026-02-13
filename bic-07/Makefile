bic.exe: bic.cs
	mcs -pkg:dotnet -r:System.Windows.Forms -out:bic.exe bic.cs

run:
	mono bic.exe

clean:
	rm bic.exe

.PHONY: run clean

