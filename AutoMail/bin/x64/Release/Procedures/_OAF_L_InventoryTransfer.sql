CREATE PROCEDURE "_OAF_L_InventoryTransfer" (IN Docentry INTEGER)
AS
BEGIN

DirRange =
    SELECT "DocEntry", "DocLineNum",
           MAX("ActionType") AS "HighAT",
           MIN("ActionType") AS "LowAT"
    FROM OILM
    WHERE "TransType" = 67 AND "DocEntry" = :Docentry
    GROUP BY "DocEntry", "DocLineNum";

SELECT
T0."DocEntry",T0."DocNum",T0."DocType",T0."CANCELED",T0."DocStatus",T0."DocDate",T0."DocDueDate",T0."Comments",
T0."JrnlMemo",T0."U_TruckID",T0."U_TruckContact",T0."U_PLUS_TYPE",T0."U_MINUS_TYPE",T0."U_ITType",
T0."U_OdooID",T0."U_DriverNumber",T0."U_DriverID",T0."U_SecurityName",T0."U_SenderName" AS "SenderName",T0."U_SecurityID",T0."U_ReceiverName",
T0."U_DispatchingWhse",T0."U_TransferType",T0."U_AuthorizedBy",T0."U_Inv_Movt_Reasons",T0."U_ChannelID",
T0."U_TrailerNumber",T0."U_OdooDispatchID",T0."U_Waybill",
T1."DocEntry" AS "WTR1.DocEntry",T1."LineNum",T1."LineStatus",T1."ItemCode",T1."Dscription",T1."Quantity" AS "Doc_Qty_Raw",T1."OpenQty",T1."Currency",T1."FisrtBin",T1."FromWhsCod",T1."WhsCode",
T1."OcrCode2" AS "Department",T1."OcrCode3" AS "Location",T1."OcrCode4" AS "Grant",T1."UomEntry",T1."UomCode",T1."U_WebPRID",T1."U_Requestor",
FB."BinCode" AS "WhseBin",
T1."U_TruckID" AS "LineTruckId",T1."U_TruckContact" AS "LineTruckContact",

/* FROM side. Category qty columns (GOODQTY etc.) show the TO bin when it is a bin-to-bin transfer (same warehouse), otherwise the FROM bin */
IFNULL(FB."BinID",0)                         AS "BinID",
IFNULL(FB."BinCode",'')                      AS "BinCode",
IFNULL(FB."SL1Code",'')                      AS "SL1Code",
IFNULL(FB."BinQty",0)                        AS "BinQty",
CASE WHEN T1."FromWhsCod" = T1."WhsCode" THEN IFNULL(TB."DAMAGEDQTY",0) ELSE IFNULL(FB."DAMAGEDQTY",0) END AS "DAMAGEDQTY",
CASE WHEN T1."FromWhsCod" = T1."WhsCode" THEN IFNULL(TB."DESTROYEDQTY",0) ELSE IFNULL(FB."DESTROYEDQTY",0) END AS "DESTROYEDQTY",
CASE WHEN T1."FromWhsCod" = T1."WhsCode" THEN IFNULL(TB."GOODQTY",0) ELSE IFNULL(FB."GOODQTY",0) END AS "GOODQTY",
CASE WHEN T1."FromWhsCod" = T1."WhsCode" THEN IFNULL(TB."WARRANTYQTY",0) ELSE IFNULL(FB."WARRANTYQTY",0) END AS "WARRANTYQTY",
CASE WHEN T1."FromWhsCod" = T1."WhsCode" THEN IFNULL(TB."TRANSITQTY",0) ELSE IFNULL(FB."TRANSITQTY",0) END AS "TRANSITQTY",
CASE WHEN T1."FromWhsCod" = T1."WhsCode" THEN IFNULL(TB."LOSSQTY",0) ELSE IFNULL(FB."LOSSQTY",0) END AS "LOSSQTY",

/* TO side */
IFNULL(TB."BinID",0)                         AS "ToBinID",
IFNULL(TB."BinCode",'')                      AS "ToBinCode",
IFNULL(TB."SL1Code",'')                      AS "ToSL1Code",
IFNULL(TB."BinQty",0)                        AS "ToBinQty",
IFNULL(TB."DAMAGEDQTY",0)                    AS "TODAMAGEDQTY",
IFNULL(TB."DESTROYEDQTY",0)                  AS "TODESTROYEDQTY",
IFNULL(TB."GOODQTY",0)                       AS "TOGOODQTY",
IFNULL(TB."WARRANTYQTY",0)                   AS "TOWARRANTYQTY",
IFNULL(TB."TRANSITQTY",0)                    AS "TOTRANSITQTY",
IFNULL(TB."LOSSQTY",0)                       AS "TOLOSSQTY",

TO_VARCHAR(
    CASE
        WHEN (SELECT M0."InvntryUom" FROM OITM M0 WHERE M0."ItemCode" = T1."ItemCode") = 'Item'
        THEN ROUND(T1."Quantity", 0)
        WHEN IFNULL((SELECT M0."IWeight1" FROM OITM M0 WHERE M0."ItemCode" = T1."ItemCode"), 0) > 0
        THEN ROUND((T1."Quantity" * 1000) /
            (SELECT M0."IWeight1" FROM OITM M0 WHERE M0."ItemCode" = T1."ItemCode"),3)
        ELSE ROUND(T1."Quantity", 3) END)||
CASE
    WHEN (SELECT M0."InvntryUom" FROM OITM M0 WHERE M0."ItemCode" = T1."ItemCode") = 'Item' THEN ' PCs'
    ELSE IFNULL((SELECT M1."UnitDisply" FROM OITM M0 INNER JOIN OWGT M1 ON M0."IWght1Unit" = M1."UnitCode"
         WHERE M0."ItemCode" = T1."ItemCode"),' PCs') END AS "Item_Weight",

ATT."SecuritySign",ATT."ReceiverSign",ATT."SenderSign",ATT."DriverSign"

FROM OWTR T0
INNER JOIN WTR1 T1 ON T0."DocEntry" = T1."DocEntry"

/* FROM bins (ActionType = HighAT) */
LEFT JOIN (
    SELECT L."DocEntry", L."DocLineNum", B."WhsCode",
           MAX(BL."BinAbs")                       AS "BinID",
           STRING_AGG(B."BinCode", ', ')          AS "BinCode",
           MAX(B."SL1Code")                       AS "SL1Code",
           SUM(BL."Quantity")                     AS "BinQty",
           SUM(CASE WHEN B."SL1Code"='DAMAGED'   THEN BL."Quantity" ELSE 0 END) AS "DAMAGEDQTY",
           SUM(CASE WHEN B."SL1Code"='DESTROYED' THEN BL."Quantity" ELSE 0 END) AS "DESTROYEDQTY",
           SUM(CASE WHEN B."SL1Code"='GOOD'      THEN BL."Quantity" ELSE 0 END) AS "GOODQTY",
           SUM(CASE WHEN B."SL1Code"='WARRANTY'  THEN BL."Quantity" ELSE 0 END) AS "WARRANTYQTY",
           SUM(CASE WHEN B."SL1Code"='TRANSIT'   THEN BL."Quantity" ELSE 0 END) AS "TRANSITQTY",
           SUM(CASE WHEN B."SL1Code"='LOSS'      THEN BL."Quantity" ELSE 0 END) AS "LOSSQTY"
    FROM OILM L
    INNER JOIN OBTL BL ON BL."MessageID" = L."MessageID"
    INNER JOIN OBIN B  ON B."AbsEntry"   = BL."BinAbs"
    INNER JOIN :DirRange DX ON DX."DocEntry" = L."DocEntry" AND DX."DocLineNum" = L."DocLineNum"
    WHERE L."TransType" = 67 AND L."ActionType" = DX."HighAT"
    GROUP BY L."DocEntry", L."DocLineNum", B."WhsCode"
) FB ON FB."DocEntry" = T1."DocEntry" AND FB."DocLineNum" = T1."LineNum"
    AND FB."WhsCode" = T1."FromWhsCod"

/* TO bins (ActionType = LowAT) */
LEFT JOIN (
    SELECT L."DocEntry", L."DocLineNum", B."WhsCode",
           MAX(BL."BinAbs")                       AS "BinID",
           STRING_AGG(B."BinCode", ', ')          AS "BinCode",
           MAX(B."SL1Code")                       AS "SL1Code",
           SUM(BL."Quantity")                     AS "BinQty",
           SUM(CASE WHEN B."SL1Code"='DAMAGED'   THEN BL."Quantity" ELSE 0 END) AS "DAMAGEDQTY",
           SUM(CASE WHEN B."SL1Code"='DESTROYED' THEN BL."Quantity" ELSE 0 END) AS "DESTROYEDQTY",
           SUM(CASE WHEN B."SL1Code"='GOOD'      THEN BL."Quantity" ELSE 0 END) AS "GOODQTY",
           SUM(CASE WHEN B."SL1Code"='WARRANTY'  THEN BL."Quantity" ELSE 0 END) AS "WARRANTYQTY",
           SUM(CASE WHEN B."SL1Code"='TRANSIT'   THEN BL."Quantity" ELSE 0 END) AS "TRANSITQTY",
           SUM(CASE WHEN B."SL1Code"='LOSS'      THEN BL."Quantity" ELSE 0 END) AS "LOSSQTY"
    FROM OILM L
    INNER JOIN OBTL BL ON BL."MessageID" = L."MessageID"
    INNER JOIN OBIN B  ON B."AbsEntry"   = BL."BinAbs"
    INNER JOIN :DirRange DX ON DX."DocEntry" = L."DocEntry" AND DX."DocLineNum" = L."DocLineNum"
    WHERE L."TransType" = 67 AND L."ActionType" = DX."LowAT"
      AND DX."HighAT" <> DX."LowAT"
    GROUP BY L."DocEntry", L."DocLineNum", B."WhsCode"
) TB ON TB."DocEntry" = T1."DocEntry" AND TB."DocLineNum" = T1."LineNum"
    AND TB."WhsCode" = T1."WhsCode"

/* Signature attachments */
LEFT JOIN (
    SELECT
        "AbsEntry",
        MAX(CASE
            WHEN UPPER("FileName") LIKE '%GUARD%' OR UPPER("FileName") LIKE '%SECURITY%'
            THEN "trgtPath" || '\' || "FileName" || '.' || "FileExt" END) AS "SecuritySign",
        MAX(CASE
            WHEN UPPER("FileName") LIKE '%RECEIVER%'
            THEN "trgtPath" || '\' || "FileName" || '.' || "FileExt" END) AS "ReceiverSign",
        MAX(CASE
            WHEN UPPER("FileName") LIKE '%SENDER%'
            THEN "trgtPath" || '\' || "FileName" || '.' || "FileExt" END) AS "SenderSign",
        MAX(CASE
            WHEN UPPER("FileName") LIKE '%DRIVER%'
            THEN "trgtPath" || '\' || "FileName" || '.' || "FileExt" END) AS "DriverSign"
    FROM ATC1
    GROUP BY "AbsEntry"
) ATT
ON ATT."AbsEntry" = T0."AtcEntry"

WHERE
    T0."CANCELED" = 'N' AND T0."DocEntry" = :Docentry;

END;
