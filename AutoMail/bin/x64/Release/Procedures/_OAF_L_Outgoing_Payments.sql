CREATE PROCEDURE "_OAF_L_Outgoing_Payments" (IN Docentry integer)
AS

BEGIN

 SELECT "OVPM"."Printed","OVPM"."TaxDate", "OVPM"."DocNum", "OVPM"."DocCurr", "OVPM"."DocTotal", "OVPM"."CheckSum", "OVPM"."TrsfrSum", "OVPM"."CashSum",
"OVPM"."JrnlMemo", "OVPM"."Comments", "OVPM"."DocTotalSy", "OVPM"."DocTotalFC", "OVPM"."CheckSumFC", "OVPM"."TrsfrSumFC",
"OVPM"."CashSumFC","OVPM"."CardName", "OVPM"."Address", "OVPM"."DocEntry", "OVPM"."CreditSum",
"OVPM"."CredSumFC", "OVPM"."DiffCurr", "OVPM"."CardCode", "OVPM"."CounterRef", "OVPM"."VatSum", "OVPM"."VatSumSy",
"OVPM"."VatSumFC", "OVPM"."DdctSum", "OVPM"."DdctSumFC", "VPM2"."InvType", "OINV"."DocDate" "OINV.DocDate",
"OPCH"."DocDate" "OPCH.DocDate", "ORPC"."DocDate" "RPCDocDate", "ODPO"."DocDate" "DPO.DocDate", "OVPM"."DocType", "VPM2"."SumApplied",
"VPM2"."AppliedSys", "VPM2"."AppliedFC","VPM2"."InvType" "VPM2.InvType",
"OINV"."DocCur", "ORIN"."DocCur" "ORIN.DocCur", "OPCH"."DocCur" "OPCH.DocCur", "ORPC"."DocCur" "ORPC.DocCur", "ODPI"."DocCur" "ODPI.DocCur",
 "ODPO"."DocCur" "ODPO.DocCur",
"ODPS"."DeposCurr", "OVPM"."DocCurr" "OVPM.DocCurr",
"JDT1"."FCCurrency", "VPM2"."DocEntry" "VPM2.DocEntry", "ODPI"."NumAtCard" "ODPI.NumAtCard", "ODPO"."U_VendorInvNo" "ODPO.NumAtCard",
"OINV"."NumAtCard" "OINV.NumAtCard",
"OPCH"."U_VendorInvNo" "OPCH.NumAtCard",
"ORIN"."U_VendorInvNo" "ORIN.NumAtCard", "ORPC"."NumAtCard" "ORPC.NumAtCard",
"OVPM"."DocDate" "OVPM.DocDate", "ODPI"."DocDate" "ODPI.DocDate",
 "OJDT"."DueDate", "ORIN"."DocDate" "ORIN.DocDate",
"OCRD"."LicTradNum", "NNM1"."BeginStr", "NNM1"."EndStr", "ODPI"."DocNum" "ODPI.DocNum",
"ODPO"."DocNum" "ODPO.DocNum", "OINV"."DocNum" "OINV.DocNum",
"OJDT"."Number", "OPCH"."DocNum" "OPCH.DocNum", "ORIN"."DocNum" "ORIN.DocNum", "ORPC"."DocNum" "ORPC.DocNum", "ODPS"."TransAbs",
"OVPM"."U_CITIExportDate", "OVPM"."U_PayMethd", "OVPM"."U_BANK_ACCT_NO", "OVPM"."U_PAYEE_NAME",("OVPM"."U_BeyonicStatus" || "OVPM"."U_Paycode") AS "Pay_Conf_Code",
"OVPM"."U_BANK_CODE", "OVPM"."U_IN_BANK", "OPCH"."WTSum", "OPCH"."WTSumFC", "ODPO"."WTSum" "ODPO.WtSum",OVPM."WtSum" "OVPM.WtSum",
OVPM."WtSumFrgn",OVPM."NoDocSumFC",OVPM."NoDocSum",
"ODPO"."WTSumFC" "ODPO.WTSumFC", "OPCH"."DocTotal" "OPCH.DocTotal", "OPCH"."DocTotalFC" "OPCH.DocTotalFC",
"ORPC"."DocTotal" "ORPC.DocTotal", "ORPC"."DocTotalFC" "ORPC.DocTotalFC",
"OJDT"."LocTotal", "OJDT"."FcTotal", "ODPO"."DocTotal" "ODPO.DocTotal", "ODPO"."DocTotalFC" "ODPO.DocTotalFC",
"OVPM"."U_PMCode", "OVPM"."U_PM_DESC", "ADP1"."Remark",OADM."MainCurncy",
OADM."SysCurrncy",OADM."ThousSep",OADM."SumDec","DSC1"."AcctName",OVPM."TrsfrDate", OVPM."TrsfrRef",
"OADM"."CompnyName", "OADM"."CompnyAddr", "OADM"."CharMonth", "OADM"."DateFormat", "OADM"."DateSep",
"OADM"."DecSep", "OADM"."CurOnRight", "OADM"."Manager"

 FROM   {oj ((((((((((((("OVPM" "OVPM"
 LEFT OUTER JOIN "ADP1" "ADP1" ON "OVPM"."ObjType"="ADP1"."ObjType")
 LEFT OUTER JOIN "OCRD" "OCRD" ON "OVPM"."CardCode"="OCRD"."CardCode")
 LEFT OUTER JOIN "NNM1" "NNM1" ON "OVPM"."Series"="NNM1"."Series")
 LEFT OUTER JOIN "VPM2" "VPM2" ON "OVPM"."DocEntry"="VPM2"."DocNum")
 LEFT OUTER JOIN "OJDT" "OJDT" ON "VPM2"."DocTransId"="OJDT"."TransId")
 LEFT OUTER JOIN "ODPS" "ODPS" ON "VPM2"."DocTransId"="ODPS"."TransAbs")
 LEFT OUTER JOIN "ORIN" "ORIN" ON "VPM2"."DocTransId"="ORIN"."TransId")
 LEFT OUTER JOIN "JDT1" "JDT1" ON ("VPM2"."DocTransId"="JDT1"."TransId") AND ("VPM2"."DocLine"="JDT1"."Line_ID"))
 LEFT OUTER JOIN "ODPI" "ODPI" ON "VPM2"."DocEntry"="ODPI"."DocEntry")
 LEFT OUTER JOIN "ODPO" "ODPO" ON "VPM2"."DocEntry"="ODPO"."DocEntry")
 LEFT OUTER JOIN "ORPC" "ORPC" ON "VPM2"."DocTransId"="ORPC"."TransId")
 LEFT OUTER JOIN "OPCH" "OPCH" ON "VPM2"."DocTransId"="OPCH"."TransId")
 LEFT OUTER JOIN "DSC1" "DSC1" ON "OVPM"."TrsfrAcct"="DSC1"."GLAccount")
 LEFT OUTER JOIN "OINV" "OINV" ON "VPM2"."DocEntry"="OINV"."DocEntry"},OADM,OADP
 WHERE  "OVPM"."DocEntry"=:Docentry and "OVPM"."U_UPLOAD_STS" = 'UPLOADED' and "OVPM"."U_PAY_STATUS" = 'Upload for Payment'
 ORDER BY "OVPM"."DocNum";

 END;
