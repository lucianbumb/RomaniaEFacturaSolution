# Documentație ANAF RO e-Factura pentru Dezvoltatori

Acest document conține resursele și link-urile necesare pentru a construi o aplicație care generează facturi, le trimite la ANAF și le descarcă pe baza CUI-ului.

## 1. Generarea facturilor (Formatul XML / RO_CIUS)
Pentru a crea o factură, trebuie să generezi un fișier XML care respectă standardul european UBL și normele naționale RO_CIUS.

* **Specificațiile Tehnice Centrale**: [Pagina de informații tehnice e-Factura a Ministerului Finanțelor](https://mfinante.gov.ro/web/efactura/informatii-tehnice).
  * Aici vei găsi structura XML (schemele XSD), regulile de validare, exemple de facturi, tabelul de coduri de taxe și valute, precum și aplicații de validare locală a fișierelor XML înainte de a le trimite.

## 2. Autentificarea aplicației (OAuth 2.0)
Aplicația ta va comunica cu ANAF folosind un API securizat. Pentru a face acest lucru, trebuie mai întâi să obții un token de acces (prin intermediul unui certificat digital calificat).

* **Procedura OAuth**: [Procedura înregistrare aplicații portal ANAF (PDF)](https://static.anaf.ro/static/10/Anaf/Informatii_R/API/Oauth_procedura_inregistrare_aplicatii_portal_ANAF.pdf).
* **Înrolare API**: Înregistrarea aplicației (pentru a obține *Client ID* și *Client Secret*) se face în [portalul ANAF pentru servicii online (Înregistrare API)](https://www.anaf.ro/anaf/internet/ANAF/servicii_online/inreg_api).

## 3. API-urile pentru Trimitere și Descărcare (Swagger Docs)
ANAF oferă interfețe Swagger pentru testarea și înțelegerea fiecărui endpoint al API-ului RO e-Factura (atât pentru mediul de Test, cât și pentru Producție). 

* **Trimitere (Upload) Factură**: [Swagger Încărcare Factură](https://mfinante.gov.ro/static/10/eFactura/upload.html)
* **Interogare Listă Mesaje**: (Facturi primite/trimise per CUI pe o anumită perioadă de timp) [Swagger Interogare Listă Mesaje](https://mfinante.gov.ro/static/10/eFactura/listamesaje.html#/)
* **Obținere Stare Mesaj (Id Descărcare)**: [Swagger Obținere Stare](https://mfinante.gov.ro/static/10/eFactura/staremesaj.html#/)
* **Descărcare Factură**: (Arhivă ZIP cu XML original și sigiliu electronic) [Swagger Descărcare Factură](https://mfinante.gov.ro/static/10/eFactura/descarcare.html)

## 4. Fluxul general recomandat de implementare
1. **Generarea fișierului XML** conform specificațiilor tehnice.
2. **Trimiterea** acestuia către endpoint-ul de `Upload`.
3. **Salvarea** acelui `index_incarcare` primit în urma trimiterii.
4. **Interogarea** endpoint-ului `Stare Mesaj` cu indexul de încărcare pentru a obține `id_descarcare` (acesta va fi disponibil doar după ce ANAF procesează și validează XML-ul asincron).
5. **Descărcarea** arhivei cu factura finală (validată și cu semnătura MF).
