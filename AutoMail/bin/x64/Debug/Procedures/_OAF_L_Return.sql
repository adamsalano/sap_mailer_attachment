CREATE PROCEDURE "_OAF_L_Return" (IN Docentry INTEGER)
AS
BEGIN

SELECT
T0."DocEntry",T0."DocNum",T0."DocType",T0."CANCELED",T0."DocStatus",T0."DocDate",T0."DocDueDate",T0."Comments",
T0."JrnlMemo",T0."U_TruckID",T0."U_TruckContact",T0."U_PLUS_TYPE",T0."U_MINUS_TYPE",T0."U_ITType",
T0."U_OdooID",T0."U_DriverNumber",T0."U_DriverID",T0."U_SecurityName",T0."U_SenderName" AS "SenderName",T0."U_SecurityID",T0."U_ReceiverName",
T0."U_DispatchingWhse",T0."U_TransferType",T0."U_AuthorizedBy",T0."U_Inv_Movt_Reasons",T0."U_ChannelID",
T0."U_TrailerNumber",T0."U_OdooDispatchID",T0."U_Waybill",
T1."DocEntry" AS "DLN1.DocEntry",T1."LineNum",T1."LineStatus",T1."ItemCode",T1."Dscription",T1."Quantity",T1."OpenQty",T1."Currency",T1."WhsCode",
T1."OcrCode2" AS "Department",T1."OcrCode3" AS "Location",T1."OcrCode4" AS "Grant",T1."UomEntry",T1."UomCode",T1."U_WebPRID",T1."U_Requestor",
T1."U_TruckID" AS "LineTruckId",T1."U_TruckContact" AS "LineTruckContact",
''AS "BinID",''AS "BinCode",''AS "SL1Code",0 AS "BinQty",
IFNULL(BIN_DAMAGED."BinQty",0) AS "DAMAGEDQTY" ,IFNULL( BIN_DESTROYED."BinQty" ,0) AS "DESTROYEDQTY",
IFNULL( BIN_GOOD."BinQty" ,0) AS "GOODQTY",IFNULL( BIN_WARRANTY."BinQty" ,0) AS "WARRANTYQTY",
IFNULL( BIN_TRANSIT."BinQty" ,0) AS "TRANSITQTY",IFNULL( BIN_LOSS."BinQty" ,0) AS "LOSSQTY",
TO_VARCHAR(CASE WHEN (SELECT M0."InvntryUom" FROM OITM M0 WHERE M0."ItemCode" = T1."ItemCode") = 'Item' THEN ROUND(T1."Quantity", 0)
WHEN IFNULL((SELECT M0."IWeight1" FROM OITM M0 WHERE M0."ItemCode" = T1."ItemCode"),0) > 0 THEN ROUND( T1."Quantity" * (SELECT M0."IWeight1" FROM OITM M0 WHERE M0."ItemCode" = T1."ItemCode"), 3)
ELSE ROUND(T1."Quantity", 3) END)|| CASE WHEN (SELECT M0."InvntryUom" FROM OITM M0 WHERE M0."ItemCode" = T1."ItemCode") = 'Item' THEN ' PCs'
ELSE IFNULL((SELECT M1."UnitDisply" FROM OITM M0 INNER JOIN OWGT M1 ON M0."IWght1Unit" = M1."UnitCode" WHERE M0."ItemCode" = T1."ItemCode"),' PCs')END AS "Item_Weight",

ATT."SecuritySign",ATT."ReceiverSign",ATT."SenderSign",ATT."DriverSign"

FROM ORDN T0

INNER JOIN RDN1 T1 ON T0."DocEntry" = T1."DocEntry"

LEFT JOIN(
    SELECT
T2."DocEntry",T2."DocLineNum",MAX(T3."BinAbs") AS "BinID", STRING_AGG(
    T4."BinCode" || '(' || T3."Quantity" || ')',
    ', ') AS "BinCode",MAX(T4."SL1Code") AS "SL1Code",SUM(T3."Quantity") AS "BinQty"
FROM OILM T2
INNER JOIN OBTL T3 ON T3."MessageID" = T2."MessageID"
INNER JOIN OBIN T4 ON T4."AbsEntry" = T3."BinAbs"
    WHERE T2."TransType" = 16 AND T4."SL1Code" IN('DAMAGED')
    GROUP BY
        T2."DocEntry",T2."DocLineNum"
) BIN_DAMAGED
ON BIN_DAMAGED."DocEntry" = T1."DocEntry" AND BIN_DAMAGED."DocLineNum" = T1."LineNum"

LEFT JOIN(
    SELECT
T2."DocEntry",T2."DocLineNum",MAX(T3."BinAbs") AS "BinID", STRING_AGG(
    T4."BinCode" || '(' || T3."Quantity" || ')',
    ', ') AS "BinCode",MAX(T4."SL1Code") AS "SL1Code",SUM(T3."Quantity") AS "BinQty"
FROM OILM T2
INNER JOIN OBTL T3 ON T3."MessageID" = T2."MessageID"
INNER JOIN OBIN T4 ON T4."AbsEntry" = T3."BinAbs"
    WHERE T2."TransType" = 16 AND T4."SL1Code" IN('GOOD')
    GROUP BY
        T2."DocEntry",T2."DocLineNum"
) BIN_GOOD
ON BIN_GOOD."DocEntry" = T1."DocEntry" AND BIN_GOOD."DocLineNum" = T1."LineNum"
LEFT JOIN(
    SELECT
T2."DocEntry",T2."DocLineNum",MAX(T3."BinAbs") AS "BinID", STRING_AGG(
    T4."BinCode" || '(' || T3."Quantity" || ')',
    ', ') AS "BinCode",MAX(T4."SL1Code") AS "SL1Code",SUM(T3."Quantity") AS "BinQty"
FROM OILM T2
INNER JOIN OBTL T3 ON T3."MessageID" = T2."MessageID"
INNER JOIN OBIN T4 ON T4."AbsEntry" = T3."BinAbs"
    WHERE T2."TransType" = 16 AND T4."SL1Code" IN('TRANSIT')
    GROUP BY
        T2."DocEntry",T2."DocLineNum"
) BIN_TRANSIT
ON BIN_TRANSIT."DocEntry" = T1."DocEntry" AND BIN_TRANSIT."DocLineNum" = T1."LineNum"

LEFT JOIN(
    SELECT
T2."DocEntry",T2."DocLineNum",MAX(T3."BinAbs") AS "BinID", STRING_AGG(
    T4."BinCode" || '(' || T3."Quantity" || ')',
    ', ') AS "BinCode",MAX(T4."SL1Code") AS "SL1Code",SUM(T3."Quantity") AS "BinQty"
FROM OILM T2
INNER JOIN OBTL T3 ON T3."MessageID" = T2."MessageID"
INNER JOIN OBIN T4 ON T4."AbsEntry" = T3."BinAbs"
    WHERE T2."TransType" = 16 AND T4."SL1Code" IN('LOSS')
    GROUP BY
        T2."DocEntry",T2."DocLineNum"
) BIN_LOSS
ON BIN_LOSS."DocEntry" = T1."DocEntry" AND BIN_LOSS."DocLineNum" = T1."LineNum"

LEFT JOIN(
    SELECT
T2."DocEntry",T2."DocLineNum",MAX(T3."BinAbs") AS "BinID", STRING_AGG(
    T4."BinCode" || '(' || T3."Quantity" || ')',
    ', ') AS "BinCode",MAX(T4."SL1Code") AS "SL1Code",SUM(T3."Quantity") AS "BinQty"
FROM OILM T2
INNER JOIN OBTL T3 ON T3."MessageID" = T2."MessageID"
INNER JOIN OBIN T4 ON T4."AbsEntry" = T3."BinAbs"
    WHERE T2."TransType" = 16 AND T4."SL1Code" IN('WARRANTY')
    GROUP BY
        T2."DocEntry",T2."DocLineNum"
) BIN_WARRANTY
ON BIN_WARRANTY."DocEntry" = T1."DocEntry" AND BIN_WARRANTY."DocLineNum" = T1."LineNum"


LEFT JOIN(
    SELECT
T2."DocEntry",T2."DocLineNum",MAX(T3."BinAbs") AS "BinID", STRING_AGG(
    T4."BinCode" || '(' || T3."Quantity" || ')',
    ', ') AS "BinCode",MAX(T4."SL1Code") AS "SL1Code",SUM(T3."Quantity") AS "BinQty"
FROM OILM T2
INNER JOIN OBTL T3 ON T3."MessageID" = T2."MessageID"
INNER JOIN OBIN T4 ON T4."AbsEntry" = T3."BinAbs"
    WHERE T2."TransType"= 16 AND T4."SL1Code" IN('DESTROYED')
    GROUP BY
        T2."DocEntry",T2."DocLineNum"
) BIN_DESTROYED
ON BIN_DESTROYED."DocEntry" = T1."DocEntry" AND BIN_DESTROYED."DocLineNum" = T1."LineNum"

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
