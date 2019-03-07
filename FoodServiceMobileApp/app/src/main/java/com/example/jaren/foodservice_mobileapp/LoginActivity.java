package com.example.jaren.foodservice_mobileapp;

import android.support.v7.app.AppCompatActivity;
import android.os.Bundle;
import android.content.Intent;
import android.view.View;
import android.widget.Button;
import android.widget.TextView;
import android.widget.EditText;
import android.util.Log;
import android.content.SharedPreferences;
import android.widget.Toast;

import java.util.HashMap;
import java.util.Map;

public class LoginActivity extends AppCompatActivity {
    private EditText Name;
    private EditText Password;
    private TextView Info;
    private Button Login;
    private Button Register;

    private int NoOfAttempts = 999;

    public String urlLogin = "http://10.0.2.2:50577/api/user/login";
    public String urlUserType = "http://10.0.2.2:50577/api/user/getusertype";

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_login);

        Name = (EditText) findViewById(R.id.etPackageName);
        Password = (EditText) findViewById(R.id.etBusinessUsername);
        Login = (Button) findViewById(R.id.btnLogin);
        Register = (Button) findViewById(R.id.btnRegister);



        Login.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View view) {
                ValidateLogin(Name.getText().toString(), Password.getText().toString());
            }
        });

        Register.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View view) {
                Intent registerIntent = new Intent(LoginActivity.this, RegisterActivity.class);
                startActivity(registerIntent);
            }
        });
    }

    private void EnableLogin()
    {
            Login.setEnabled(true);
    }

    private void ValidateLogin(String userName, String userPassword){
        Login.setEnabled(false);

        Map<String, String> params = new HashMap<>();
        params.put("username", userName);
        params.put("password", userPassword);

        HttpPostAsyncTask request = new HttpPostAsyncTask(params, new LoginCallback());
        request.execute(urlLogin);
    }

    private void show_failed_toast()
    {

        int length = Toast.LENGTH_SHORT;
        Toast invalid_login = Toast.makeText(this, "Invalid Username\\Password",length);
        invalid_login.show();

    }

    private class LoginCallback implements HttpPostAsyncTask.Callback {
        public void onPostExecute(HttpPostCallbackResult result) {
            if(result.statusCode == 200) {
                try {
                    String token = result.jsonObj.getString("session_token");
                    SharedPreferences session = getSharedPreferences("food_service_session", MODE_PRIVATE);
                    SharedPreferences.Editor sesEdit = session.edit();
                    sesEdit.putString("token", token);
                    sesEdit.apply();

                    // Request user type
                    HttpPostAsyncTask request = new HttpPostAsyncTask(new HashMap<String, String>(), new UserTypeCallback(), token);
                    request.execute(urlUserType);
                } catch(org.json.JSONException e) {
                    Log.d("exception", e.getLocalizedMessage());
                    EnableLogin();
                }
            }
            else {
                Name.getText().clear();
                Password.getText().clear();
                show_failed_toast();
                EnableLogin();
            }
        }
    }

    private class UserTypeCallback implements HttpPostAsyncTask.Callback {
        public void onPostExecute(HttpPostCallbackResult result) {
            if(result.statusCode == 200) {
                try {
                    String userType = result.jsonObj.getString("user_type");
                    SharedPreferences session = getSharedPreferences("food_service_session", MODE_PRIVATE);
                    SharedPreferences.Editor sesEdit = session.edit();
                    sesEdit.putString("user_type", userType);
                    sesEdit.apply();

                    Intent menuIntent = null;

                    if(userType.equals("client")) {
                        menuIntent = new Intent(LoginActivity.this, ClientMenuActivity.class);
                    } else if(userType.equals("business")) {
                        menuIntent = new Intent(LoginActivity.this, BusinessMenuActivity.class);
                    } else {
                        Log.d("login", "Unknown user type");
                        Name.getText().clear();
                        Password.getText().clear();
                        show_failed_toast();
                        EnableLogin();
                    }

                    if(menuIntent != null) {
                        startActivity(menuIntent);
                    }
                }
                catch (org.json.JSONException e) {
                    Log.d("exception", e.getLocalizedMessage());
                    Name.getText().clear();
                    Password.getText().clear();
                    show_failed_toast();
                    EnableLogin();
                }
            }
            else {
                Log.d("login", "Could not get user type");
                Name.getText().clear();
                Password.getText().clear();
                show_failed_toast();
                EnableLogin();
            }
        }
    }
}
