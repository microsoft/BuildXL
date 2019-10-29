// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

// To generate lexer/parser/... run:
//
//   java -cp $CLASSPATH:antlr-4.7.2-complete.jar -Xmx500M \
//      org.antlr.v4.Tool \
//      -listener -visitor -Dlanguage=CSharp -package BuildXL.Execution.Analyzer.JPath JPath.g4

grammar JPath;

WS      : [ \t\r\n]+ -> skip    ; // skip spaces, tabs, newlines

// logic operators
NOT     : 'not';
AND     : 'and';
OR      : 'or' ;
XOR     : 'xor';
IFF     : 'iff';

// bool operators
GTE     : '>=' ;
LTE     : '<=' ;
GT      : '>'  ;
LT      : '<'  ;
EQ      : '='  ;
NEQ     : '!=' ;
MATCH   : '~'  ;
NMATCH  : '!~' ;

// arithmetic operators
MINUS   : '-'  ;
PLUS    : '+'  ;
TIMES   : '*'  ;
DIV     : '/'  ;
MOD     : '%'  ;

// array operators
CONCAT    : '++' ;
INTERSECT : '&'  ;

IntLit
    : '-'?[0-9]+ ;

StrLit
    : '\'' ~[']* '\''
    | '"' ~["]* '"'
    ;

RegExLit
    : '/' ~[/]+ '/' 
    | '!' ~[!]+ '!'
    ;

fragment IdFragment
    : [a-zA-Z][a-zA-Z0-9_]* ;

PropertyId
    : IdFragment ;

VarId
    : '$' IdFragment ;

EscID
    : '`' ~[`]+ '`' ;

Opt
    : '-' [a-zA-Z0-9'-']+ ;

intBinaryOp
    : Token=(PLUS | MINUS | TIMES | DIV | MOD) ;

intUnaryOp
    : Token=MINUS ;

boolBinaryOp
    : Token=(GTE | GT | LTE | LT | EQ | NEQ | MATCH | NMATCH) ;
    
logicBinaryOp
    : Token=(AND | OR | XOR | IFF) ; 

logicUnaryOp
    : Token=NOT ;

arrayBinaryOp
    : Token=(CONCAT | INTERSECT) ;

anyBinaryOp
    : intBinaryOp
    | boolBinaryOp
    | logicBinaryOp
    | arrayBinaryOp
    ;

intExpr
    : Expr=expr                                       #ExprIntExpr
    | Op=intUnaryOp Sub=intExpr                       #UnaryIntExpr
    | Lhs=intExpr Op=intBinaryOp Rhs=intExpr          #BinaryIntExpr
    | '(' Sub=intExpr ')'                             #SubIntExpr
    ;

boolExpr
    : Lhs=intExpr Op=boolBinaryOp Rhs=intExpr         #BinaryBoolExpr
    | '(' Sub=boolExpr ')'                            #SubBoolExpr
    ;

logicExpr
    : Expr=boolExpr                                   #BoolLogicExpr
    | Lhs=logicExpr Op=logicBinaryOp Rhs=logicExpr    #BinaryLogicExpr
    | Op=logicUnaryOp Sub=logicExpr                   #UnaryLogicExpr
    | '(' Sub=logicExpr ')'                           #SubLogicExpr
    ;

prop
    : PropertyName=PropertyId                         #PropertyId
    | PropertyName=EscID                              #EscId
    ;

selector
    : Name=prop                                       #IdSelector
    | '(' Names+=prop ('+' Names+=prop)+ ')'          #UnionSelector
    ;

literal
    : Value=StrLit                                    #StrLitExpr
    | Value=RegExLit                                  #RegExLitExpr
    | Value=IntLit                                    #IntLitExpr
    ;

propVal
    : (Name=prop ':')? Value=expr                     #PropertyValue
    ;

objLit
    : '{' Props+=propVal (',' Props+=propVal)* '}'    #ObjLitProps
    ;    

expr
    : '$'                                             #RootExpr
    | Var=VarId                                       #VarExpr
    | Sub=selector                                    #SelectorExpr
    | Obj=objLit                                      #ObjLitExpr
    | Lit=literal                                     #LiteralExpr
    | Lhs=expr '.' Sub=expr                           #MapExpr
    | Lhs=expr '[' Filter=logicExpr ']'               #FilterExpr
    | Lhs=expr '[' Index=intExpr ']'                  #IndexExpr
    | Lhs=expr '[' Begin=intExpr '..' End=intExpr ']' #RangeExpr
    | '#' Sub=expr                                    #CardinalityExpr 
    // cardinality of the result, i.e., the number of elements in it
    | Func=expr '(' Args+=expr (',' Args+=expr)* ')'  #FuncAppExprParen
    | Func=expr OptName=Opt (OptValue=literal)?       #FuncOptExpr
    | Input=expr '|' Func=expr                        #PipeExpr
    | Input=expr '|>' File=StrLit                     #SaveToFileExpr
    | Input=expr '|>>' File=StrLit                    #AppendToFileExpr
    | Lhs=expr Op=anyBinaryOp Rhs=expr                #BinExpr
    | '(' Sub=expr ')'                                #SubExpr
    | 'let' Var=VarId ':=' Val=expr 'in' Sub=expr?    #LetExpr 
    | Var=VarId ':=' Val=expr ';'?                    #AssignExpr
    ;

