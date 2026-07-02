# Opera2Oris ინტეგრაციის დოკუმენტაცია v0.1

ეს დოკუმენტი აღწერს Opera/Fidelio BOF export `*.csv` ფაილების წაკითხვას, მონაცემების მოდელებად გარდაქმნას, Oris Accounting Web API-ში ასატვირთ მოდელს და პროცესის მუშაობას ჩვეულებრივ და კრიტიკულ სიტუაციებში.

## 1. სისტემის დანიშნულება

Opera2Oris იღებს Opera/Fidelio-დან ექსპორტირებულ ფინანსურ ფაილებს, კითხულობს მათ `Headers.txt` ლექსიკონის მიხედვით, გარდაქმნის შიდა BOF მოდელად, შემდეგ ქმნის Oris Accounting Web API-ის `Transaction` მოთხოვნებს.

ძირითადი პროექტებია:

- `Opera2Oris.Entities` - CSV/BOF მოდელები, OA API მოდელები და BOF to OA კონვერტორი.
- `Opera2Oris.Domain` - CSV ფაილების და `Headers.txt` ლექსიკონის წამკითხველი worker.
- `Opera2Oris.ApiClient` - Oris Accounting Web API-ის HTTP client.
- `Opera2Oris.Middlewear` - მთავარი პროცესი: კონფიგურაცია, folder listener, გარდაქმნა, outbox და upload retry.

## 2. CSV ფაილები

BOF export ფაილები ინახება კონფიგურაციაში მითითებულ საქაღალდეში:

```json
"BofExport": {
  "SourceDirectory": "D:\\Projects\\Opera2Oris\\BOF Exoprt",
  "HeaderDictionaryPath": "Headers.txt",
  "SearchPattern": "*.csv",
  "Delimiter": ";",
  "EncodingName": "windows-1251"
}
```

`SourceDirectory` არის მხოლოდ Opera/Fidelio export CSV ფაილების folder. `appsettings.json` და `Headers.txt` არის service configuration files და უნდა იდოს service/exe folder-ში ერთად. თუ `HeaderDictionaryPath` არის relative path, ის ითვლება `appsettings.json`-ის მდებარეობიდან.

ფაილები არის CSV ფორმატის, მაგრამ სათაურები თვითონ ფაილში არ წერია. პირველი ხაზი არის `REC`, შემდეგ მოდის მონაცემის ხაზები. სვეტების სახელები და რიგითობა მოდის service folder-ში არსებულ `Headers.txt` ფაილიდან.

მაგალითები:

- `BOF_CD.csv` - charges daily, ანუ მომსახურებები დღიური ექსპორტით.
- `BOF_CO.csv` - charges by check-out, ანუ გასვლის მიხედვით.
- `BOF_CU.csv` - charges until today, ანუ თვიდან დღემდე.
- `BOF_KD.csv` - packages daily.
- `BOF_KO.csv` - packages by check-out.
- `BOF_KU.csv` - packages until today.
- `BOF_PD.csv` - payments daily.
- `BOF_PO.csv` - payments by check-out.
- `BOF_PU.csv` - payments until today.

ფაილის suffix განსაზღვრავს კატეგორიას და scope-ს:

| Suffix| კატეგორია	| Scope 		|
|---	|---		|---			|
| `CD` 	| Charge  	| Daily 		|
| `CO` 	| Charge  	| CheckOut 		|
| `CU` 	| Charge  	| UntilToday 	|
| `KD` 	| Package 	| Daily 		|
| `KO` 	| Package 	| CheckOut 		|
| `KU` 	| Package 	| UntilToday 	|
| `PD` 	| Payment 	| Daily 		|
| `PO` 	| Payment 	| CheckOut 		|
| `PU` 	| Payment 	| UntilToday 	|

## 3. როგორ იკითხება და იპარსება CSV

CSV parsing კეთდება `Opera2Oris.Domain` პროექტში, `BofExportWorker` კლასით.

პროცესი:

1. სისტემა კითხულობს `HeaderDictionaryPath`-ით მითითებულ `Headers.txt` ფაილს.
2. `Headers.txt`-ში ყოველი ხაზი მსგავსი ფორმატით არის:

```text
--  01 resort
--  02 trx_no
--  03 trx_no_added_by
```

3. ლექსიკონიდან იქმნება 118 სვეტის აღწერა: ordinal, სახელი და სტაბილური key.
4. შემდეგ worker კითხულობს ყველა `*.csv` ფაილს `SourceDirectory`-დან.
5. CSV parser იყენებს კონფიგურირებულ delimiter-ს, ამჟამად `;`.
6. parser სწორად ამუშავებს quoted field-ებს, მაგალითად `"VAT"` ან `"2026-02-16"`.
7. `REC` marker ხაზი გამოტოვებულია.
8. ცარიელი ხაზები გამოტოვებულია.
9. თუ ველი ნაკლებია ვიდრე `Headers.txt`-ში მითითებული სვეტების რაოდენობა, ჩანაწერი არ იტვირთება და warning იწერება.
10. თუ ხაზი malformed არის, პროცესი არ ჩერდება; იქმნება warning და შემდეგი ხაზები მუშავდება.

ამჟამინდელ sample export-ში `BOF_CD.csv`-ს ბოლო ხაზზე აქვს stray quote (`"`). სისტემა ამას აფიქსირებს warning-ად და აგრძელებს დანარჩენი ფაილების დამუშავებას.

## 4. CSV/BOF მოდელი

BOF მოდელები მდებარეობს `Opera2Oris.Entities/Bof/Import`.

### `BofColumnDefinition`

აღწერს ერთ CSV სვეტს:

- `Ordinal` - სვეტის რიგითი ნომერი `Headers.txt`-დან.
- `Name` - ორიგინალი სახელი, მაგალითად `trx_no`.
- `Key` - კოდში გამოსაყენებელი სტაბილური სახელი.

### `BofFieldValue`

ინახავს ერთი ველის მნიშვნელობას:

- `Column` - სვეტის აღწერა.
- `RawValue` - ფაილიდან წაკითხული string მნიშვნელობა.

აქვს helper მეთოდები ტიპებად გადასაყვანად:

- `AsString()`
- `AsInt64()`
- `AsDecimal()`
- `AsDate()`
- `AsTime()`
- `AsDateTime()`

მაგალითად BOF-ში რიცხვი შეიძლება იყოს `.000000000000`; მოდელი ამას სწორად კითხულობს როგორც `0`.

### `BofExportRecord`

ერთი CSV data row-ის მოდელია. ინახავს:

- source file path/name;
- source line number;
- category (`Charge`, `Package`, `Payment`);
- scope (`Daily`, `CheckOut`, `UntilToday`);
- ყველა 118 field-ს.

ასევე აქვს convenience property-ები ხშირად გამოყენებული ველებისთვის:

- `TransactionNumber`
- `ParentTransactionNumber`
- `TransactionCode`
- `TransactionDescription`
- `BusinessDate`
- `TransactionAmount`
- `Currency`
- `FolioNumber`
- `GuestName`
- `Room`
- `GuestAccountCredit`
- `GuestAccountDebit`
- `PackageCredit`
- `PackageDebit`
- `PaymentMethod`
- `PostedAmount`
- `SystemDate`
- `SystemTime`

### `BofExportFile`

ერთი ფაილის შედეგი:

- file path/name;
- category;
- scope;
- records;
- warnings.

### `BofExportBatch`

ერთი პროცესინგის batch:

- source directory;
- header dictionary path;
- column definitions;
- files;
- warnings;
- record count;
- warning count.

## 5. OA API მოდელი

OA API მოდელები მდებარეობს `Opera2Oris.Entities/Oa`.

### Login მოდელი

`OaLoginRequest` შეესაბამება `POST /api/LogIn` endpoint-ს.

მთავარი ველები:

- `user`
- `password`
- `language`
- `databaseName`
- `databaseUserName`
- `databaseUserPassword`
- `databaseIsLocal`
- `localDatabasePath`
- `sqlServer`
- `useWindowsAuthentication`

პასუხი არის `OaLoginResponse`:

```json
{
  "token": "..."
}
```

token გამოიყენება ყველა შემდეგ API მოთხოვნაში.

### Transaction მოდელი

ერთი BOF record გარდაიქმნება ერთ `OaTransactionRequest` მოდელად, რომელიც შეესაბამება `POST /api/Transaction` endpoint-ს. თითო request-ში იქმნება ერთი `transactionEntries` row.

მთავარი ველები:

- `token`
- `transactionComment`
- `transactionDate`
- `transactionDocumentNumber`
- `correctDisbalance`
- `transactionEntries`

`transactionDocumentNumber` ფორმირდება ასე:

```text
OPERA-{BOF transaction number}
```

მაგალითად:

```text
OPERA-104367130
```

ეს მნიშვნელოვანია idempotency-სთვის, რადგან იგივე BOF transaction number არ უნდა აიტვირთოს ორჯერ.

### Transaction Entry მოდელი

`OaTransactionEntryRequest` არის ერთი OA entry row.

მთავარი ველები:

- `mainEntry`
- `account`
- `debitAmount`
- `creditAmount`
- `currency`
- `costCentre`
- `costUnit`
- `cashFlow`
- `comment`

კონვერტორი თითო BOF record-იდან ქმნის ერთ entry-ს. JSON-ში ორივე amount field იღებს ერთსა და იმავე absolute amount value-ს:

- თუ თანხა დადებითია: `debitAmount = amount`, `creditAmount = amount`.
- თუ თანხა უარყოფითია: `debitAmount = abs(amount)`, `creditAmount = abs(amount)`.

## 6. BOF to OA გარდაქმნა

გარდაქმნა ხდება `BofToOaTransactionConverter` კლასით.

კონვერტორი არ იგონებს account number-ებს, რადგან Oris Accounting-ის account chart არის კომპანიის სპეციფიკური. ამიტომ საჭიროა mapping `appsettings.json`-ში.

Mapping config:

```json
"Mapping": {
  "GuestLedgerAccount": "",
  "PackageLedgerAccount": "",
  "DefaultRevenueAccount": "",
  "DefaultPaymentAccount": "",
  "DefaultCurrency": "GEL",
  "DefaultCostCentre": "",
  "DefaultCostUnit": "",
  "CashFlow": "",
  "DocumentNumberPrefix": "OPERA-",
  "UseBusinessDate": true,
  "CorrectDisbalance": false,
  "RevenueAccountsByTransactionCode": {},
  "RevenueAccountsByTransactionSubGroup": {},
  "PaymentAccountsByTransactionCode": {},
  "PaymentAccountsByMethod": {}
}
```

### Account mapping ველების მნიშვნელობა

`Mapping`-ში მითითებული account-ები განსაზღვრავს ერთ `transactionEntries[0].account` value-ს. converter არ აგზავნის BOF-ის raw account ნომრებს, რადგან Oris-ის chart of accounts კომპანიის მიხედვით იცვლება.

| Config field | როდის გამოიყენება | OA entry |
|---|---|---|
| `GuestLedgerAccount` | ყველა `Charge` ჩანაწერზე | `transactionEntries[0].account` |
| `GuestLedgerAccount` | `Package` ჩანაწერზე, თუ `PackageLedgerAccount` ცარიელია | `transactionEntries[0].account` fallback |
| `PackageLedgerAccount` | `Package` ჩანაწერზე, თუ შევსებულია | `transactionEntries[0].account` |
| `DefaultPaymentAccount` | `Payment` ჩანაწერზე, თუ `trx_code` და `payment_method` mapping ვერ მოიძებნა | `transactionEntries[0].account` fallback |

`DefaultRevenueAccount`, `RevenueAccountsByTransactionCode` და `RevenueAccountsByTransactionSubGroup` მიმდინარე single-entry conversion-ში არ გამოიყენება. ისინი config-ში შეიძლება დარჩეს future double-entry რეჟიმისთვის, მაგრამ payload-ის შექმნაზე გავლენა არ აქვს.

Payment account აირჩევა მხოლოდ `Payment` ჩანაწერებისთვის. არჩევის პრიორიტეტია:

1. `PaymentAccountsByTransactionCode[trx_code]`
2. `PaymentAccountsByMethod[payment_method]`
3. `DefaultPaymentAccount`

თუ თანხა უარყოფითია, converter account-ს იგივე წესით პოულობს. OA-ში ორივე amount field-ში იგზავნება absolute value.

ცარიელი string-ები (`""`) და ცარიელი dictionary entry-ები არ გამოიყენება mapping-ში.

მარტივი single-entry mapping:

```json
"Mapping": {
  "GuestLedgerAccount": "GE87ZZ0000000000000101",
  "PackageLedgerAccount": "GE60ZZ0000000000000102",
  "DefaultRevenueAccount": "",
  "DefaultPaymentAccount": "GE49ZZ0000000000000300",
  "DefaultCurrency": "GEL",
  "DefaultCostCentre": "",
  "DefaultCostUnit": "",
  "CashFlow": "",
  "DocumentNumberPrefix": "OPERA-",
  "UseBusinessDate": true,
  "CorrectDisbalance": false,
  "RevenueAccountsByTransactionCode": {},
  "RevenueAccountsByTransactionSubGroup": {},
  "PaymentAccountsByTransactionCode": {},
  "PaymentAccountsByMethod": {}
}
```

ამ რეჟიმში `Charge` ჩანაწერები იყენებს `GuestLedgerAccount`-ს, `Package` ჩანაწერები `PackageLedgerAccount`-ს, ხოლო `Payment` ჩანაწერები `DefaultPaymentAccount`-ს ან payment-specific mapping-ს. ზემოთ მოცემული `GE...` მნიშვნელობები არის test IBAN-style placeholders და production Oris account-ებად არ უნდა ჩაითვალოს.

### დამოკიდებულია თუ არა mapping CSV ფაილის ტიპზე

დიახ, მაგრამ მხოლოდ accounting logic-ის დონეზე.

`Headers.txt` არ განსაზღვრავს transaction-ის ტიპს. ის განსაზღვრავს CSV სვეტების რიგითობას და სახელებს. ყველა BOF CSV ფაილი იკითხება ერთი და იგივე dictionary-ით: მაგალითად ordinal `2` არის `trx_no`, ordinal `5` არის `trx_code`, ordinal `113` არის `posted_amount`.

CSV ფაილის ტიპი განისაზღვრება ფაილის suffix-ით:

| ფაილი 		| Category 	| Scope 		| როგორ მოქმედებს mapping-ზე 										|
|---			|---		|---			|---																|
| `BOF_CD.csv` 	| `Charge` 	| `Daily` 		| ქმნის ერთ entry-ს `GuestLedgerAccount` account-ით 			|
| `BOF_CO.csv` 	| `Charge` 	| `CheckOut` 	| იგივე Charge accounting logic; scope რჩება audit/comment-ში 		|
| `BOF_CU.csv` 	| `Charge` 	| `UntilToday` 	| იგივე Charge accounting logic; scope რჩება audit/comment-ში 		|
| `BOF_KD.csv` 	| `Package` | `Daily` 		| ქმნის ერთ entry-ს `PackageLedgerAccount` ან `GuestLedgerAccount` account-ით 	|
| `BOF_KO.csv` 	| `Package` | `CheckOut` 	| იგივე Package accounting logic; scope რჩება audit/comment-ში 		|
| `BOF_KU.csv` 	| `Package` | `UntilToday` 	| იგივე Package accounting logic; scope რჩება audit/comment-ში 		|
| `BOF_PD.csv` 	| `Payment` | `Daily` 		| ქმნის ერთ entry-ს payment account-ით 	|
| `BOF_PO.csv` 	| `Payment` | `CheckOut` 	| იგივე Payment accounting logic; scope რჩება audit/comment-ში 		|
| `BOF_PU.csv` 	| `Payment` | `UntilToday` 	| იგივე Payment accounting logic; scope რჩება audit/comment-ში 		|

ანუ პირველი ასო (`C`, `K`, `P`) მოქმედებს ერთი entry account-ის არჩევაზე. მეორე ასო (`D`, `O`, `U`) ამჟამად არ ცვლის account mapping-ს; ის გამოიყენება file category/scope-ის გასარჩევად.

### გარდაქმნის ნაბიჯები

ერთი `BofExportRecord` გარდაიქმნება ერთ `OaTransactionRequest`-ად. ამ OA transaction-ში იქმნება ერთი `transactionEntries` item.

entry-ში `debitAmount` და `creditAmount` ორივე იღებს ერთსა და იმავე absolute amount value-ს. მაგალითად `24000` თანხაზე ორივე ველი იქნება `24000`.

თუ თანხა ნულია ან ვერ მოიძებნა, record არ იგზავნება API-ში და იწერება warning. თუ ერთი entry account ვერ მოიძებნა mapping config-ით, record ასევე არ იგზავნება API-ში.

### Amount mapping

BOF export-ში თანხა შეიძლება სხვადასხვა სვეტში იყოს შევსებული. ამიტომ converter იყენებს პრიორიტეტს:

| პრიორიტეტი | BOF field | Ordinal | რატომ გამოიყენება |
|---|---|---:|---|
| 1 | `posted_amount` | 113 | უკვე posted accounting amount არის და ყველაზე პირდაპირ ასახავს დასადებ თანხას |
| 2 | `trx_amount` | 10 | transaction-ის ძირითადი თანხა, თუ posted amount ცარიელია |
| 3 | `guest_account_debit` | 63 | guest ledger debit მოძრაობა, ხშირად charge/package სცენარში |
| 4 | `guest_account_credit` | 62 | guest ledger credit მოძრაობა, ხშირად payment/reversal სცენარში |
| 5 | `package_debit` | 65 | package ledger debit მოძრაობა |
| 6 | `package_credit` | 64 | package ledger credit მოძრაობა |
| 7 | `gross_amount` | 60 | gross fallback, თუ ledger-specific amount-ები არ არის |
| 8 | `net_amount` | 61 | ბოლო fallback, თუ მხოლოდ net amount არსებობს |

ამ წესის მიზანია, რომ პირველ რიგში გამოყენებულ იქნას ყველაზე accounting-ready value და მხოლოდ შემდეგ fallback amount-ები.

### Account mapping წესები

Charge ჩანაწერებისთვის:

- `transactionEntries[0].account` არის `GuestLedgerAccount`.

amount-ის ნიშანი აღარ განსაზღვრავს debit/credit მხარეს: absolute amount იწერება ორივე ველში.

Package ჩანაწერებისთვის:

- `transactionEntries[0].account` არის `PackageLedgerAccount`, თუ შევსებულია;
- თუ `PackageLedgerAccount` ცარიელია, გამოიყენება `GuestLedgerAccount`.

ეს საჭიროა, რადგან ზოგ კომპანიაში packages ცალკე ledger-ზე აღირიცხება, ზოგში კი პირდაპირ guest ledger-ზე.

Payment ჩანაწერებისთვის:

- `transactionEntries[0].account` არის payment account, მაგალითად cash/bank/card account.

Payment account აირჩევა ამ პრიორიტეტით:

1. `PaymentAccountsByTransactionCode`
2. `PaymentAccountsByMethod`
3. `DefaultPaymentAccount`

### Field to field mapping

ეს ცხრილი არის transformation map და არა strict one-to-one database mapping. ამიტომ `OA target` სვეტში ერთი და იგივე target შეიძლება რამდენჯერმე გამოჩნდეს:

- რამდენიმე BOF date/time ველი ავსებს ერთ `transactionDate`-ს fallback პრიორიტეტით.
- რამდენიმე BOF amount ველი ავსებს ერთ accounting amount-ს fallback პრიორიტეტით, ხოლო OA entry-ში ამ amount-ის absolute value იწერება როგორც `debitAmount`-ში, ისე `creditAmount`-ში.
- `transactionComment` იღებს CSV-ის comment text-ს (`description`, fallback `remark`, fallback `reference`); debtor/payee name იწერება entry comment-ში.
- account target-ები პირდაპირ BOF-დან არ კოპირდება; ისინი ირჩევა `Mapping` config-ით.

| BOF source | OA target | როდის გამოიყენება | რატომ არის ასე mapped |
|---|---|---|---|
| `trx_no` ordinal `2` | `transactionDocumentNumber` | ყველა category | ქმნის stable document number-ს `DocumentNumberPrefix + trx_no`; საჭიროა idempotency-სთვის, რომ იგივე BOF transaction ორჯერ არ აიტვირთოს |
| `business_date` ordinal `8` | `transactionDate` | default, როცა `UseBusinessDate = true` | hotel accounting-ში business date ხშირად არის სწორი accounting date, თუნდაც system/posting დრო სხვა იყოს |
| `trx_date` ordinal `79` | `transactionDate` fallback | თუ business date არ არის ან `UseBusinessDate = false` | ინახავს transaction-ის ოპერაციულ თარიღს |
| `posting_date` ordinal `80` | `transactionDate` fallback | თუ business/trx date ცარიელია | posting date არის შემდეგი სანდო accounting date |
| `insert_date` ordinal `7` | `transactionDate` fallback | თუ სხვა თარიღები ცარიელია | ბოლო fallback, რომ OA transaction date ცარიელი არ დარჩეს |
| `posting_time` ordinal `81` | `transactionDate` time ნაწილი | თუ არსებობს | OA date/time-ს აძლევს posting-ის დროს |
| `insert_time` ordinal `9` | `transactionDate` time fallback | თუ posting time ცარიელია | ინარჩუნებს ჩანაწერის შექმნის დროს |
| amount fields `113`, `10`, `63`, `62`, `65`, `64`, `60`, `61` | `transactionEntries[0].debitAmount` და `creditAmount` | ყველა category | არჩეული amount-ის absolute value იწერება ორივე ველში |
| `currency` ordinal `13` | `transactionEntries[].currency` | თუ BOF-ში currency შევსებულია | transaction currency უნდა მიყვეს Opera-ს currency-ს |
| `DefaultCurrency` config | `transactionEntries[].currency` fallback | თუ BOF currency ცარიელია | სისტემის default currency არის `GEL` |
| `trx_code` ordinal `5` | account lookup | Charge/Package revenue mapping; Payment payment mapping | transaction code ყველაზე ზუსტი გასაღებია account mapping-ისთვის |
| `trx_subgroup` ordinal `59` | currently not used for account lookup | Charge/Package | single-entry რეჟიმში revenue mapping არ გამოიყენება |
| `payment_method` ordinal `92` | payment account lookup fallback | Payment | payment method უკავშირებს payment-ს cash/bank/card account-ს |
| `payee_name` ordinal `20` | `transactionEntries[].comment` | თუ შევსებულია | debtor/payee name ჩანს entry comment-ში |
| `guest_name` ordinal `24` | `transactionEntries[].comment` fallback | თუ `payee_name` ცარიელია | guest name გამოიყენება debtor/payee name fallback-ად |
| `name_type` ordinal `21` | არ იგზავნება comment-ში | information only | sample value `D` არის name type, არა first name |
| `description` ordinal `6` | `transactionComment` და `transactionEntries[].comment` | თუ შევსებულია | OA header comment არის CSV transaction description, მაგალითად `VAT`, `Cash`, `Pre Stay Charge`; entry comment-ში description ემატება debtor/payee name-ს |
| `remark` ordinal `83` | `transactionComment` და `transactionEntries[].comment` fallback | თუ `description` ცარიელია | CSV-ის remark text ჩანს OA transaction-ში |
| `reference` ordinal `84` | `transactionComment` და `transactionEntries[].comment` ბოლო fallback | თუ `description` და `remark` ცარიელია | ინახავს Opera reference-ს, თუ სხვა comment text არ არის |
| source file name | payload dump file name | ყველა category | dump file name-ში ჩანს რომელი CSV-დან შეიქმნა JSON |
| source file name და source line | outbox/source metadata | ყველა category | audit/debug-ისთვის რჩება ტექნიკურ metadata-ში, მაგრამ OA comment-ში აღარ ემატება |
| `DefaultCostCentre` config | `transactionEntries[].costCentre` | თუ config შევსებულია | კომპანიის შიდა analytic/accounting განაწილებისთვის |
| `DefaultCostUnit` config | `transactionEntries[].costUnit` | თუ config შევსებულია | cost unit analytic-ისთვის |
| `CashFlow` config | `transactionEntries[].cashFlow` | თუ config შევსებულია | OA cash-flow classification-ისთვის |
| `CorrectDisbalance` config | `correctDisbalance` | ყველა transaction | აკონტროლებს OA API-ის disbalance correction behavior-ს |
| API token | `token` | upload დროს | authentication-ისთვის; outbox-ში token არ ინახება |

### Comment mapping

`transactionComment` აღარ იქმნება audit/context ტექსტის გაერთიანებით. comment-ში არ ემატება source file, line, category, room ან transaction code.

Debtor/payee name აირჩევა CSV ველებიდან ამ პრიორიტეტით:

1. `payee_name` ordinal `20`
2. `guest_name` ordinal `24`

Comment text აირჩევა CSV ველებიდან ამ პრიორიტეტით:

1. `description` ordinal `6`
2. `remark` ordinal `83`
3. `reference` ordinal `84`

header comment ფორმატია:

```text
transactionComment = {description || remark || reference}
```

entry comment ფორმატია:

```text
transactionEntries[0].comment = {payee_name || guest_name} - transactionComment
```

თუ debtor/payee name ცარიელია, entry comment-ში დარჩება მხოლოდ `transactionComment`. თუ comment text ცარიელია, header comment ცარიელი იქნება და entry comment-ში დარჩება მხოლოდ debtor/payee name. Header-ზე და entry-ზე ერთი და იგივე value აღარ იწერება: header ინახავს description-ს, entry ინახავს debtor name + description-ს.

Audit ინფორმაცია source CSV-ზე და line-ზე ინახება outbox/source metadata-ში. Payload dump file name-ში ინახება source CSV-ის სახელი და `transactionComment`, მაგრამ source line Oris comment-ში აღარ იგზავნება.

ვალუტის default არის `GEL`.

## 7. სრული process flow

### Startup flow

1. `Opera2Oris.Middlewear` კითხულობს `appsettings.json`-ს.
2. თუ `Outbox.ProcessPendingOnStart = true`, ჯერ იტვირთება pending outbox ჩანაწერები.
3. თუ `Watch.Enabled = true`, ჯერ იწყება folder listener.
4. თუ `Watch.ProcessExistingOnStart = true`, listener startup-ზე პოულობს უკვე არსებულ CSV ფაილებს და თითოეულს გეგმავს `Watch.DebounceSeconds` delay-ით.
5. თუ პროგრამა გაეშვა `--once` რეჟიმში, ის ასევე ელოდება `Watch.DebounceSeconds` delay-ს და შემდეგ ამუშავებს არსებულ CSV ფაილებს.
6. პარალელურად მუშაობს outbox retry loop.

### Folder listener flow

1. სისტემა უსმენს `BofExport.SourceDirectory` საქაღალდეს.
2. listener-ის ჩართვის შემდეგ, თუ `Watch.ProcessExistingOnStart = true`, უკვე არსებულ CSV ფაილებსაც იგივე queue-ში ამატებს.
3. ახალი, შეცვლილი ან startup-ზე ნაპოვნი CSV ფაილი ელოდება debounce პერიოდს, default `5` წამს.
4. ელოდება სანამ ფაილი აღარ არის lock-ში.
5. კითხულობს კონკრეტულ ფაილს.
6. ქმნის BOF batch-ს.
7. გარდაქმნის final OA API request payload-ებად; თითო CSV row ქმნის ერთ request-ს.
8. თუ `PayloadDump.Enabled = true`, final API request JSON-ებს წერს dump folder-ში.
9. თუ `OaWebApi.UploadEnabled = true`, იგივე final request-ებს წერს durable outbox-ში.
10. თუ archive ჩართულია, source CSV იკუმშება `.zip` ფაილად archive folder-ში და `DeleteSourceAfterArchive = true` შემთხვევაში იშლება source folder-იდან.
11. თუ upload ჩართულია, შემდეგ ცდილობს outbox-ის pending ჩანაწერების API-ში ატვირთვას.

### Logging flow

ყველა მთავარი მოქმედება იწერება `ProcessLogger`-ით:

- startup და config path;
- folder listener-ის დაწყება და file events;
- CSV ფაილის წაკითხვა, batch summary და warnings;
- conversion შედეგები;
- payload dump შედეგები;
- outbox-ში ჩაწერა და pending queue-ის დამუშავება;
- API upload attempt/result/error;
- archive attempt/result/error;
- retry loop-ის დაწყება, გამოტოვება და შეცდომები.

ლოგი იწერება console-ში და, თუ `Logging.Enabled = true`, ასევე ყოველდღიურ log ფაილში. ფაილის სახელი არის:

```text
{Logging.FilePrefix}-yyyyMMdd.log
```

მაგალითად:

```text
logs/opera2oris-20260630.log
```

### Archive flow

დამუშავებული CSV ფაილები ინახება compressed archive-ში `ProcessedFileArchiver`-ით.

`Archive.Directory` relative path ითვლება `BofExport.SourceDirectory`-დან. default `"archive"` ნიშნავს `{SourceDirectory}/archive`.

არქივირება ხდება მხოლოდ მას შემდეგ, რაც CSV-დან მიღებული payload-ები უსაფრთხოდ ჩაიწერება მიმდინარე რეჟიმისთვის საჭირო durable output-ში:

- test/dry-run რეჟიმში: `PayloadDump.Directory`;
- upload რეჟიმში: durable outbox.

ეს მნიშვნელოვანია API offline სცენარისთვის: თუ API არ მუშაობს, მონაცემი უკვე outbox-ში ან payload dump-შია შენახული და source CSV შეიძლება გადავიდეს archive-ში.

ნორმალური flow:

1. CSV იკითხება `BofExport.SourceDirectory`-დან.
2. ჩანაწერები გარდაიქმნება final OA API request payload-ებად; თითო CSV row ქმნის ერთ request-ს.
3. final request-ები იწერება payload dump-ში ან outbox-ში.
4. თუ archive ჩართულია, source CSV იკუმშება `.zip` ფაილად `Archive.Directory`-ში, რომელიც default-ად source folder-ის ქვეშ არის.
5. თუ `Archive.DeleteSourceAfterArchive = true`, zip-ის წარმატებით შექმნის შემდეგ source CSV იშლება source folder-იდან.

ამჟამინდელ config-ში `Archive.ArchiveOnlyWithoutWarnings = false`, ამიტომ warning-იანი CSV-ც გადადის archive-ში. თუ production-ში გინდათ warning-იანი ფაილი source folder-ში დარჩეს operator/developer შემოწმებისთვის, დააყენეთ `ArchiveOnlyWithoutWarnings = true`.

ამჟამინდელ config-ში `Archive.ArchiveWhenUploadDisabled = true`, ამიტომ dry-run/test რეჟიმშიც source CSV გადადის archive-ში payload dump-ის შექმნის შემდეგ.

### Payload dump / dry-run flow

`PayloadDump` გამოიყენება მაშინ, როცა OA API ჯერ არ არის მზად, მაგრამ გვჭირდება ვნახოთ ზუსტად რა `OaTransactionRequest` objects შეიქმნებოდა `POST /api/Transaction` endpoint-სთვის.

`PayloadDump.Directory` relative path ითვლება `BofExport.SourceDirectory`-დან. default `"payload-dump"` ნიშნავს `{SourceDirectory}/payload-dump`.

flow:

1. CSV იკითხება `BofExport.SourceDirectory`-დან.
2. BOF records გარდაიქმნება final `OaTransactionRequest` payload-ებად; თითო CSV row ქმნის ერთ request-ს.
3. თუ `PayloadDump.Enabled = true`, თითო final API request იწერება ცალკე JSON ფაილად.
4. dump კეთდება `OaWebApi.UploadEnabled` შემოწმებამდე, ამიტომ `UploadEnabled = false` რეჟიმშიც final JSON ფაილები იქმნება.
5. JSON dump არ შეიცავს API token-ს და არ შეიცავს source metadata-ს (`SourceFilePath`, `SourceLineNumber`), რადგან ეს ველები API request body-ის ნაწილი არ არის.

ფაილის სახელი შედგება source CSV-ის სახელისგან, `transactionComment`-ისგან და `transactionDocumentNumber`-ისგან, მაგალითად:

```text
BOF_PO.csv_Cash_OP-104280115.json
```

test რეჟიმისთვის რეკომენდებული config:

```json
"PayloadDump": {
  "Enabled": true,
  "Directory": "payload-dump"
},
"OaWebApi": {
  "UploadEnabled": false
}
```

### Upload flow

1. თუ `OaWebApi.UploadEnabled = false`, upload არ ხდება. თუ `PayloadDump.Enabled = true`, final JSON ფაილები უკვე ჩაწერილია dump folder-ში.
2. თუ upload ჩართულია, სისტემა იღებს token-ს:
   - ან `OaWebApi.Token`-იდან;
   - ან `OaWebApi.Login` მონაცემებით `POST /api/LogIn`-იდან.
3. ყოველი pending outbox record იგზავნება `POST /api/Transaction` endpoint-ზე. თითო record არის ერთი final `OaTransactionRequest`.
4. წარმატების შემდეგ record ინიშნება `Uploaded`.
5. წარუმატებლობისას record რჩება `Pending` ან ინიშნება `Failed`, error-ის ტიპის მიხედვით.

## 8. კრიტიკული სიტუაციები

### API offline ან timeout

ეს არის ყველაზე მნიშვნელოვანი production სცენარი.

სისტემა ჯერ წერს payload-ს outbox-ში და მხოლოდ ამის შემდეგ ცდილობს API upload-ს. თუ API გათიშულია:

- პროცესი არ კარგავს payload-ს;
- token არ იწერება დისკზე;
- record რჩება `Pending`;
- იზრდება `AttemptCount`;
- იწერება `LastError`;
- გამოითვლება შემდეგი retry დრო `NextAttemptAtUtc`;
- retry loop მოგვიანებით ხელახლა ცდის upload-ს.

Retry delay არის exponential backoff:

- იწყება `RetryBaseDelaySeconds` მნიშვნელობით;
- იზრდება ყოველ retry-ზე;
- არ აჭარბებს `RetryMaxDelaySeconds`-ს.

ამ სცენარში processed CSV archive-ში გადადის მხოლოდ outbox-ში ჩაწერის შემდეგ. თუ API offline არის, archive არ ნიშნავს მონაცემის დაკარგვას: upload-ისთვის საჭირო payload უკვე outbox-შია და retry loop მოგვიანებით განაგრძობს ატვირთვას.

### Logging ან archive write error

თუ log ფაილში ჩაწერა დროებით ვერ მოხერხდა, პროცესი console logging-ს მაინც აგრძელებს.

თუ archive zip-ის შექმნა ვერ მოხერხდა:

- source CSV არ იშლება;
- error იწერება log-ში;
- outbox record-ები რჩება disk-ზე;
- upload/retry flow არ ფუჭდება archive failure-ის გამო.

Archive folder-ისთვის საჭიროა საკმარისი disk space და write permission. Production-ში archive folder უნდა იყოს backup/retention პოლიტიკაში ჩართული.

### Partial upload

თუ 100 pending record-იდან 20 აიტვირთა და შემდეგ API გაითიშა:

- 20 record ინიშნება `Uploaded`;
- დარჩენილი record-ები რჩება `Pending`;
- შემდეგ retry-ზე აიტვირთება მხოლოდ pending record-ები.

ეს იცავს სისტემას მონაცემების დაკარგვისგან.

### Process restart

თუ service ან server გადაიტვირთა:

1. outbox ფაილები რჩება დისკზე;
2. startup-ზე სისტემა კითხულობს pending records;
3. ჯერ ცდილობს pending records-ის upload-ს;
4. შემდეგ ამუშავებს ახალ/არსებულ CSV ფაილებს.

### CSV malformed row

თუ CSV-ში ერთი ხაზი დაზიანებულია:

- დაზიანებული ხაზი warning-ად იწერება;
- მთელი პროცესი არ ჩერდება;
- სხვა ხაზები მუშავდება.
- თუ `ArchiveOnlyWithoutWarnings = false`, ასეთი CSV მაინც გადავა archive-ში; თუ `true`, source folder-ში დარჩება ხელით შესამოწმებლად.

### Missing account mapping

თუ converter ვერ პოულობს account mapping-ს:

- OA payload არ იქმნება;
- conversion warning იწერება;
- upload არ ხდება კონკრეტული record-ისთვის.

ეს უსაფრთხოა, რადგან არასწორ account-ზე ატვირთვა უფრო სახიფათოა, ვიდრე upload-ის შეჩერება.

### API validation error

თუ API აბრუნებს non-retryable error-ს, მაგალითად validation/account closed/missing account:

- record ინიშნება `Failed`;
- ინახება `LastError`;
- ავტომატურად აღარ გადაიტვირთება retry loop-ით.

ასეთი record საჭიროებს operator-ის ან developer-ის ჩარევას.

## 9. Outbox სტრუქტურა

Outbox საქაღალდე კონფიგურირდება ასე:

```json
"Outbox": {
  "Enabled": true,
  "Directory": "outbox",
  "ProcessPendingOnStart": true,
  "RetryLoopSeconds": 30,
  "RetryBaseDelaySeconds": 30,
  "RetryMaxDelaySeconds": 1800,
  "MaxBatchSize": 100,
  "KeepUploadedRecords": true
}
```

Outbox-ში თითო final API request ინახება ცალკე JSON ფაილად.

Record შეიცავს:

- `Id` - stable hash `transactionDocumentNumber`-იდან.
- `Status` - `Pending`, `Uploaded`, ან `Failed`.
- `SourceFilePath`
- `SourceLineNumber`
- `TransactionDocumentNumber`
- `CreatedAtUtc`
- `UpdatedAtUtc`
- `NextAttemptAtUtc`
- `AttemptCount`
- `LastError`
- `LastErrorType`
- `OaTransactionsId`
- `Payload`

API token outbox-ში არ ინახება.

## 10. appsettings.json სრული აღწერა

### `BofExport`

```json
"BofExport": {
  "SourceDirectory": "D:\\Projects\\Opera2Oris\\BOF Exoprt",
  "HeaderDictionaryPath": "Headers.txt",
  "SearchPattern": "*.csv",
  "Delimiter": ";",
  "EncodingName": "windows-1251"
}
```

- `SourceDirectory` - საქაღალდე, სადაც Opera/Fidelio CSV ფაილები ჩნდება.
- `HeaderDictionaryPath` - `Headers.txt` ფაილის გზა. relative path ითვლება `appsettings.json`-ის მდებარეობიდან. თუ ცარიელია, default არის `{appsettings.json folder}/Headers.txt`.
- `SearchPattern` - რომელი ფაილები დამუშავდეს.
- `Delimiter` - CSV delimiter. ამ export-ში არის `;`.
- `EncodingName` - ფაილის encoding. BOF documentation-ის მიხედვით default არის `windows-1251`.

Deployment rule: `appsettings.json` და `Headers.txt` უნდა იყოს service files-ის folder-ში ერთად. CSV export folder არ არის configuration folder; ის მხოლოდ incoming BOF `*.csv` ფაილებისთვის გამოიყენება.

### `Logging`

```json
"Logging": {
  "Enabled": true,
  "Directory": "logs",
  "FilePrefix": "opera2oris"
}
```

- `Enabled` - true თუ process log უნდა ჩაიწეროს file-ში.
- `Directory` - log ფაილების საქაღალდე. relative path ითვლება `appsettings.json`-ის მდებარეობიდან.
- `FilePrefix` - daily log file-ის prefix. საბოლოო ფაილის სახეა `{FilePrefix}-yyyyMMdd.log`.

### `Watch`

```json
"Watch": {
  "Enabled": true,
  "ProcessExistingOnStart": true,
  "DebounceSeconds": 5
}
```

- `Enabled` - true თუ პროცესმა უნდა მოუსმინოს folder-ს.
- `ProcessExistingOnStart` - startup-ზე არსებული CSV ფაილების დაგეგმვა იმავე worker queue-ში, რასაც ახალი ფაილები იყენებს.
- `DebounceSeconds` - რამდენი წამი დაელოდოს startup-ზე ნაპოვნ ან შეცვლილ ფაილს, სანამ დამუშავებას დაიწყებს. default არის `5`.

### `Archive`

```json
"Archive": {
  "Enabled": true,
  "Directory": "archive",
  "DeleteSourceAfterArchive": true,
  "UseDateSubfolders": true,
  "IncludeTimestampInArchiveName": true,
  "ArchiveOnlyWithoutWarnings": false,
  "ArchiveWhenUploadDisabled": true
}
```

- `Enabled` - processed CSV ფაილების zip archive-ში გადატანის ჩართვა.
- `Directory` - archive folder. relative path ითვლება `BofExport.SourceDirectory`-დან, ამიტომ `"archive"` ნიშნავს `{SourceDirectory}/archive`.
- `DeleteSourceAfterArchive` - true თუ zip-ის წარმატებით შექმნის შემდეგ source CSV უნდა წაიშალოს source folder-იდან.
- `UseDateSubfolders` - true თუ archive-ში შეიქმნება daily folder, მაგალითად `archive/2026/06/30`.
- `IncludeTimestampInArchiveName` - true თუ zip ფაილის სახელში დაემატება UTC timestamp.
- `ArchiveOnlyWithoutWarnings` - true თუ warning-იანი CSV უნდა დარჩეს source folder-ში operator/developer შემოწმებისთვის.
- `ArchiveWhenUploadDisabled` - true თუ dry-run/upload-disabled რეჟიმშიც უნდა მოხდეს archive payload dump-ის შემდეგ.

### `Outbox`

```json
"Outbox": {
  "Enabled": true,
  "Directory": "outbox",
  "ProcessPendingOnStart": true,
  "RetryLoopSeconds": 30,
  "RetryBaseDelaySeconds": 30,
  "RetryMaxDelaySeconds": 1800,
  "MaxBatchSize": 100,
  "KeepUploadedRecords": true
}
```

- `Enabled` - durable outbox-ის ჩართვა.
- `Directory` - outbox-ის ფაილების საქაღალდე.
- `ProcessPendingOnStart` - startup-ზე pending records-ის retry.
- `RetryLoopSeconds` - რამდენ წამში ერთხელ შემოწმდეს pending queue.
- `RetryBaseDelaySeconds` - პირველი retry delay.
- `RetryMaxDelaySeconds` - მაქსიმალური retry delay.
- `MaxBatchSize` - ერთ retry ციკლში მაქსიმუმ რამდენი record აიტვირთოს.
- `KeepUploadedRecords` - true თუ წარმატებით ატვირთული records audit-ისთვის დარჩეს.

### `PayloadDump`

```json
"PayloadDump": {
  "Enabled": true,
  "Directory": "payload-dump"
}
```

- `Enabled` - true თუ final OA transaction JSON ფაილები უნდა ჩაიწეროს disk-ზე.
- `Directory` - dump folder. relative path ითვლება `BofExport.SourceDirectory`-დან, ამიტომ `"payload-dump"` ნიშნავს `{SourceDirectory}/payload-dump`.
- dump იწერება conversion-ის შემდეგ და upload-მდე, ამიტომ მუშაობს `OaWebApi.UploadEnabled = false` dry-run რეჟიმშიც.
- თითო dump ფაილი არის ერთი final `OaTransactionRequest` API body-ის ფორმაში. token და source metadata არ ინახება.

### `OaWebApi`

```json
"OaWebApi": {
  "BaseAddress": "https://localhost:5501/",
  "UploadEnabled": false,
  "TimeoutSeconds": 30,
  "Token": "",
  "Login": {
    "User": "",
    "Password": "",
    "Language": "en-EN",
    "DatabaseName": "",
    "DatabaseUserName": "",
    "DatabaseUserPassword": "",
    "DatabaseIsLocal": null,
    "LocalDatabasePath": "",
    "SqlServer": "",
    "UseWindowsAuthentication": null
  },
  "Mapping": {
    "GuestLedgerAccount": "GE87ZZ0000000000000101",
    "PackageLedgerAccount": "GE60ZZ0000000000000102",
    "DefaultRevenueAccount": "",
    "DefaultPaymentAccount": "GE49ZZ0000000000000300",
    "DefaultCurrency": "GEL",
    "DefaultCostCentre": "",
    "DefaultCostUnit": "",
    "CashFlow": "",
    "DocumentNumberPrefix": "OPERA-",
    "UseBusinessDate": true,
    "CorrectDisbalance": false,
    "RevenueAccountsByTransactionCode": {},
    "RevenueAccountsByTransactionSubGroup": {},
    "PaymentAccountsByTransactionCode": {},
    "PaymentAccountsByMethod": {}
  }
}
```

- `BaseAddress` - OA Web API-ის მისამართი.
- `UploadEnabled` - true თუ upload რეალურად უნდა შესრულდეს.
- `TimeoutSeconds` - HTTP timeout.
- `Token` - თუ token ხელით არის მითითებული, login აღარ კეთდება.
- `Login` - მონაცემები `POST /api/LogIn`-ისთვის.
- `Mapping` - BOF transaction-ების Oris account-ებზე მიბმის წესები.
- `GuestLedgerAccount` - guest/debtor ledger account. გამოიყენება `Charge` records-ზე და package fallback-ზე.
- `PackageLedgerAccount` - package account. თუ ცარიელია, package-ზე გამოიყენება `GuestLedgerAccount`.
- `DefaultRevenueAccount` - მიმდინარე single-entry conversion-ში არ გამოიყენება.
- `DefaultPaymentAccount` - payment fallback `Payment` records-ისთვის. თუ ცარიელია და specific payment mapping ვერ მოიძებნა, payment record skipped იქნება warning-ით.
- `RevenueAccountsByTransactionCode` / `RevenueAccountsByTransactionSubGroup` - მიმდინარე single-entry conversion-ში არ გამოიყენება.
- `PaymentAccountsByTransactionCode` / `PaymentAccountsByMethod` - optional detailed payment mapping.
- ზემოთ ნაჩვენები `GE...` values არის test IBAN-style placeholders.

## 11. გაშვების მაგალითები

ერთჯერადი დამუშავება:

```powershell
dotnet run --project .\Opera2Oris.Middlewear\Opera2Oris.Middlewear.csproj -- --once
```

folder listener რეჟიმი:

```powershell
dotnet run --project .\Opera2Oris.Middlewear\Opera2Oris.Middlewear.csproj
```

სხვა config ფაილით:

```powershell
dotnet run --project .\Opera2Oris.Middlewear\Opera2Oris.Middlewear.csproj -- --config "C:\Config\appsettings.json"
```

## 12. Production რეკომენდაციები

1. `Outbox.Enabled` უნდა იყოს `true`.
2. `UploadEnabled` ჩართეთ მხოლოდ მას შემდეგ, რაც account mapping სრულად შევსებულია.
3. `Outbox.Directory` განათავსეთ დისკზე, რომელიც არ იშლება deploy/restart-ისას.
4. `KeepUploadedRecords = true` სასარგებლოა audit-ისთვის.
5. მონიტორინგში შეამოწმეთ:
   - pending records რაოდენობა;
   - failed records რაოდენობა;
   - retry count;
   - LastError.
6. `DocumentNumberPrefix` არ შეცვალოთ production-ში migration-ის გარეშე, რადგან idempotency stable document number-ზეა დამოკიდებული.
7. API downtime-ის დროს არ წაშალოთ outbox ფაილები.
8. თუ record გადავიდა `Failed` სტატუსში, ჯერ გამოასწორეთ config/account/API validation პრობლემა, შემდეგ შეიძლება ხელით დაბრუნდეს `Pending` სტატუსზე.
9. `Logging.Enabled` დატოვეთ `true`, ხოლო `Logging.Directory` განათავსეთ ისეთ disk-ზე, სადაც service account-ს write permission აქვს.
10. `Archive.Directory` უნდა იყოს საკმარისი disk space-ით და backup/retention წესით.
11. `ArchiveOnlyWithoutWarnings` აირჩიეთ ოპერაციული წესით: `false` გადააქვს ყველა წაკითხული CSV archive-ში, `true` warning-იან CSV-ს source folder-ში ტოვებს ხელით შესამოწმებლად.
12. `appsettings.json` და `Headers.txt` ყოველთვის deploy-დება service/exe folder-ში ერთად.
13. რეგულარულად შეამოწმეთ log/archive/outbox/payload-dump folder-ების ზომა და retention policy.
