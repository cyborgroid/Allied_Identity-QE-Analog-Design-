/*
 * DeskScript — AlliedID 바탕화면 조립 언어
 *
 * 블록 편집기(Google Blockly)가 이 언어를 생성하고, 셸(C#)이 ANTLR 파서로 읽어
 * 바탕화면을 실시간으로 다시 조립한다. 사람이 직접 써도 된다.
 *
 *   desktop "내 책상" {
 *     mode auto;
 *     wallpaper "#14182b";
 *     sidebar left { terminal; files; packages; tasks; dialer; blocks; }
 *     widget clock at (24, 24) size (320, 140);
 *     widget terminal "Shell" at (360, 24) size (640, 420) { command "uname -a"; }
 *     circuit {
 *       led "상태" pin 17;
 *       button "긴급전화" pin 27 -> call "436846882187";
 *     }
 *   }
 */
grammar DeskScript;

file        : desktop EOF ;

desktop     : 'desktop' name=STRING '{' statement* '}' ;

statement   : modeStmt
            | wallpaperStmt
            | sidebarStmt
            | widgetStmt
            | circuitStmt
            ;

modeStmt      : 'mode' modeName=ident ';' ;                       // auto | phone | desktop
wallpaperStmt : 'wallpaper' color=STRING ';' ;
sidebarStmt   : 'sidebar' side=ident '{' sidebarItem* '}' ;    // left | right | bottom
sidebarItem   : app=ident ';' ;

widgetStmt  : 'widget' kind=ident title=STRING? 'at' pos=point 'size' size=point
              ( ';' | '{' prop* '}' )
            ;
prop        : key=ident value ';' ;

circuitStmt : 'circuit' '{' part* '}' ;
part        : kind=ident name=STRING 'pin' pin=NUMBER ( '->' action )? ';' ;
action      : verb=ident value? ;

point       : '(' x=NUMBER ',' y=NUMBER ')' ;
value       : STRING | NUMBER | ident ;

// 키워드도 이름 자리에 올 수 있다 (예: `widget circuit`, `launch desktop`)
ident       : ID | 'desktop' | 'mode' | 'wallpaper' | 'sidebar' | 'widget'
            | 'at' | 'size' | 'circuit' | 'pin'
            ;

ID          : [a-zA-Z_] [a-zA-Z0-9_.-]* ;
NUMBER      : '-'? [0-9]+ ( '.' [0-9]+ )? ;
STRING      : '"' ( '\\' . | ~["\\\r\n] )* '"' ;
LINE_COMMENT: '//' ~[\r\n]* -> skip ;
BLOCK_COMMENT: '/*' .*? '*/' -> skip ;
WS          : [ \t\r\n]+ -> skip ;
